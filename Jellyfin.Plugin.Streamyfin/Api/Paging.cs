using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Streamyfin.Api;

/// <summary>
/// One page of something the server answers whole.
/// </summary>
/// <remarks>
/// Jellyfin's own <c>/UserViews</c> ignores <c>startIndex</c> and <c>limit</c>: measured on
/// 10.11.11, asking for two rows starting at the third of three answers all three with
/// <c>StartIndex: 0</c>. The app's home rows scroll and ask for the next page as somebody
/// reaches the end of the one they have, so a row drawn straight from it repeats its
/// libraries for as long as they keep scrolling.
/// </remarks>
public static class Paging
{
    /// <summary>
    /// A page, and how much there is to page through.
    /// </summary>
    /// <typeparam name="T">What is on the page.</typeparam>
    /// <param name="Items">What is on this page.</param>
    /// <param name="Total">How many there are altogether.</param>
    /// <param name="StartIndex">Where this page starts.</param>
    public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int StartIndex);

    /// <summary>
    /// Takes one page out of everything.
    /// </summary>
    /// <typeparam name="T">What is being paged.</typeparam>
    /// <param name="all">Everything there is.</param>
    /// <param name="startIndex">Where the page starts, or nothing for the beginning.</param>
    /// <param name="limit">How many to answer with, or nothing for all of them.</param>
    /// <returns>The page, with the count of everything.</returns>
    /// <remarks>
    /// What cannot be a page is read rather than refused: a negative start is the
    /// beginning, and a limit of zero or less is nothing at all, which is what a client
    /// asking for nothing meant. Past the end is an empty page rather than the first one
    /// again, which is what makes a scrolling row stop.
    /// </remarks>
    public static Page<T> Of<T>(IReadOnlyList<T> all, int? startIndex, int? limit)
    {
        ArgumentNullException.ThrowIfNull(all);

        var from = Math.Max(0, startIndex ?? 0);

        if (limit is <= 0)
        {
            return new Page<T>([], all.Count, from);
        }

        var page = all.Skip(from);

        if (limit is { } many)
        {
            page = page.Take(many);
        }

        return new Page<T>([.. page], all.Count, from);
    }
}
