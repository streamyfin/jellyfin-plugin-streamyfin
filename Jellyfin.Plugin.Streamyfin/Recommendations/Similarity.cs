using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Streamyfin.Recommendations;

/// <summary>
/// What a thing in the library is made of, as far as telling it apart from another goes.
/// </summary>
/// <param name="Id">Which thing it is.</param>
/// <param name="Genres">Its genres.</param>
/// <param name="Tags">Its tags.</param>
/// <param name="Studios">Its studios.</param>
public readonly record struct ItemFacts(
    Guid Id,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Studios);

/// <summary>
/// How alike two things in the library are.
/// </summary>
/// <remarks>
/// <para>
/// Jellyfin 12 ships a similarity provider and lets a plugin ask it; 10.11 does not, and
/// what it does have is broken. Its <c>/Movies/Recommendations</c> builds each row by
/// querying movies with no reference to the one the row is about, so the "similar to what
/// you watched" row it returns is a random shelf with a familiar title on it.
/// </para>
/// <para>
/// So the plugin works it out itself, on both lines, rather than behaving one way on a new
/// server and another on an old one. The weights are Jellyfin's own, taken from the
/// provider 12 ships (<c>MovieSimilarItemsProvider</c>: genre 10, tag 5, studio 5), so the
/// order here is the order that server would have produced. What is left out is the cast:
/// upstream scores a shared director at 50 and a shared actor at 15, which is worth having
/// and needs a query per person rather than what the item already carries.
/// </para>
/// </remarks>
public static class Similarity
{
    /// <summary>A genre both things carry.</summary>
    public const int GenreWeight = 10;

    /// <summary>A tag both things carry.</summary>
    public const int TagWeight = 5;

    /// <summary>A studio both things come from.</summary>
    public const int StudioWeight = 5;

    /// <summary>
    /// Scores how alike two things are. Nothing in common is zero, and the more they share
    /// the higher it goes. The number means nothing on its own: it exists to put a shelf
    /// in order.
    /// </summary>
    /// <param name="seed">One of them, usually something that was watched.</param>
    /// <param name="candidate">The other, usually something that was not.</param>
    /// <returns>The score, never below zero.</returns>
    public static int Between(ItemFacts seed, ItemFacts candidate) =>
        (Shared(seed.Genres, candidate.Genres) * GenreWeight)
        + (Shared(seed.Tags, candidate.Tags) * TagWeight)
        + (Shared(seed.Studios, candidate.Studios) * StudioWeight);

    /// <summary>
    /// How many names appear on both sides. Metadata comes from scrapers and from files,
    /// so the same genre arrives capitalised differently, padded, or listed twice on one
    /// item, and none of that is a difference between two films.
    /// </summary>
    private static int Shared(IReadOnlyList<string>? mine, IReadOnlyList<string>? theirs)
    {
        if (mine is null || theirs is null || mine.Count == 0 || theirs.Count == 0)
        {
            return 0;
        }

        var names = Names(mine);
        names.IntersectWith(Names(theirs));

        return names.Count;
    }

    private static HashSet<string> Names(IReadOnlyList<string> said) =>
        said
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
