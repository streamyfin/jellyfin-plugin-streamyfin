using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Streamyfin.Recommendations;

/// <summary>
/// The shelf each person was last given, kept for a little while.
/// </summary>
/// <remarks>
/// <para>
/// Building a shelf reads everything unplayed in the genres somebody watches and scores all
/// of it. The app asks for it a page at a time as somebody scrolls, so rebuilding per page
/// would cost the server the same work several times over, and would also shuffle the shelf
/// under the scroll, since what somebody plays between two pages changes the order.
/// </para>
/// <para>
/// Ten minutes, and it is thrown away when what it was built from changes. Longer and
/// finishing a film in the evening would not show in the row until the next day; shorter
/// and a slow scroll would rebuild it.
/// </para>
/// </remarks>
public sealed class ForYouShelves
{
    /// <summary>
    /// How long a shelf stands before it is built again.
    /// </summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<(Guid User, string Asking), Lazy<Shelf>> _shelves = new();
    private readonly Func<DateTime> _now;

    /// <summary>
    /// Initializes a new instance of the <see cref="ForYouShelves"/> class.
    /// </summary>
    public ForYouShelves()
        : this(() => DateTime.UtcNow)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ForYouShelves"/> class with its own
    /// idea of the time, which is what makes going stale testable.
    /// </summary>
    /// <param name="now">What time it is.</param>
    public ForYouShelves(Func<DateTime> now)
    {
        _now = now;
    }

    /// <summary>
    /// Hands back somebody's shelf, building it if there is none or the one there has
    /// stood long enough.
    /// </summary>
    /// <param name="user">Whose shelf.</param>
    /// <param name="build">How to build it, called only when one is needed.</param>
    /// <param name="asking">
    /// What was asked for, when a request can ask for the shelf to be built differently. A
    /// shelf built one way is never handed back to a request that asked another way.
    /// </param>
    /// <returns>The shelf: ids, best guess first.</returns>
    public IReadOnlyList<Guid> For(Guid user, Func<List<Guid>> build, string asking = "")
    {
        ArgumentNullException.ThrowIfNull(build);

        var key = (user, asking);

        while (true)
        {
            // Lazy rather than the value itself, so that several requests arriving together
            // wait on one build instead of each starting their own. The app asks for the
            // row and its first page at once, which is exactly that case.
            var wanted = new Lazy<Shelf>(() => new Shelf(_now(), build()));
            var standing = _shelves.GetOrAdd(key, wanted);

            if (_now() - standing.Value.Built <= KeptFor)
            {
                return standing.Value.Items;
            }

            // Stale. Replaced rather than removed, so a request arriving now waits for the
            // new shelf rather than starting a third one.
            _shelves.TryUpdate(key, wanted, standing);
        }
    }

    /// <summary>
    /// Throws somebody's shelves away, so the next request builds a new one. Every way
    /// they asked for it goes, since what it was built from is what changed.
    /// </summary>
    /// <param name="user">Whose shelves.</param>
    public void Forget(Guid user)
    {
        foreach (var key in _shelves.Keys)
        {
            if (key.User == user)
            {
                _shelves.TryRemove(key, out _);
            }
        }
    }

    private sealed record Shelf(DateTime Built, List<Guid> Items);
}
