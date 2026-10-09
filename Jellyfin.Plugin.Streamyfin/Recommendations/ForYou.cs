using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Streamyfin.Recommendations;

/// <summary>
/// Puts a "for you" row in order.
/// </summary>
/// <remarks>
/// Jellyfin already knows what one thing is like: it is what the "more like this" row on an
/// item page is built from. What it does not do is answer that question about somebody's
/// whole history at once, which is what a row of recommendations needs.
///
/// So the plugin asks the question several times, once per thing recently watched, and this
/// merges the answers. What comes up after several different things is a better guess than
/// what comes up after one, and Jellyfin answers each question best-first, so where
/// something sits in an answer breaks the tie. Nothing here talks to the library, which is
/// what makes the order testable on its own.
/// </remarks>
public static class ForYou
{
    /// <summary>
    /// How much of one seed's answer is allowed to count.
    /// </summary>
    /// <remarks>
    /// Without a cap, one film in a crowded genre puts the whole genre on the shelf, and
    /// what several seeds agree on is buried under what one of them merely touches.
    /// </remarks>
    public const int PerSeed = 50;

    /// <summary>
    /// Builds the shelf: what somebody watched on one side, what they have not on the
    /// other.
    /// </summary>
    /// <param name="seeds">What they watched. Never suggested back to them.</param>
    /// <param name="candidates">What they might watch.</param>
    /// <param name="perSeed">How many of each seed's closest matches count.</param>
    /// <returns>Ids, best guess first. Anything sharing nothing is left out.</returns>
    public static List<Guid> Shelf(
        IReadOnlyList<ItemFacts> seeds,
        IReadOnlyList<ItemFacts> candidates,
        int perSeed = PerSeed)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(candidates);

        var watched = seeds.Select(seed => seed.Id).ToHashSet();

        var answers = seeds
            .Select(seed => (IReadOnlyList<Guid>)candidates
                .Where(candidate => !watched.Contains(candidate.Id))
                .Select(candidate => (candidate.Id, Score: Similarity.Between(seed, candidate)))
                .Where(scored => scored.Score > 0)
                .OrderByDescending(scored => scored.Score)
                .Take(perSeed)
                .Select(scored => scored.Id)
                .ToList())
            .ToList();

        return Ranked(answers, watched);
    }

    /// <summary>
    /// Merges what Jellyfin said about several things into one order.
    /// </summary>
    /// <param name="similarPerSeed">
    /// One answer per thing watched: what Jellyfin says it is like, closest first.
    /// </param>
    /// <param name="exclude">
    /// What must not be suggested. The things the questions were asked about, and anything
    /// already watched.
    /// </param>
    /// <returns>Ids, best guess first.</returns>
    public static List<Guid> Ranked(
        IReadOnlyList<IReadOnlyList<Guid>> similarPerSeed,
        IReadOnlyCollection<Guid> exclude)
    {
        ArgumentNullException.ThrowIfNull(similarPerSeed);
        ArgumentNullException.ThrowIfNull(exclude);

        var skip = exclude as IReadOnlySet<Guid> ?? new HashSet<Guid>(exclude);
        var tally = new Dictionary<Guid, Tally>();

        foreach (var similar in similarPerSeed)
        {
            // One answer naming the same thing twice is still one answer about it.
            var counted = new HashSet<Guid>();

            for (var rank = 0; rank < similar.Count; rank++)
            {
                var id = similar[rank];
                if (skip.Contains(id) || !counted.Add(id))
                {
                    continue;
                }

                tally[id] = tally.TryGetValue(id, out var soFar)
                    ? soFar with { Answers = soFar.Answers + 1, Ranks = soFar.Ranks + rank }
                    : new Tally(1, rank, tally.Count);
            }
        }

        return
        [
            .. tally
                .OrderByDescending(entry => entry.Value.Answers)
                .ThenBy(entry => entry.Value.Ranks)
                .ThenBy(entry => entry.Value.Arrived)
                .Select(entry => entry.Key)
        ];
    }

    /// <param name="Answers">How many of the questions named it.</param>
    /// <param name="Ranks">How far down those answers it sat, added up.</param>
    /// <param name="Arrived">Where it first came up, so an exact tie stays put.</param>
    private readonly record struct Tally(int Answers, int Ranks, int Arrived);
}
