using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Streamyfin.Recommendations;

/// <summary>
/// The shelf each person was last given, kept for a little while, and what they have just
/// started watching.
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

    /// <summary>
    /// How many of the things somebody just started are remembered.
    /// </summary>
    /// <remarks>
    /// This is what is playing now, not a second history: the library already knows what
    /// was watched, and what it does not know is what was started and not yet finished.
    /// </remarks>
    public const int StartsKept = 3;

    private readonly ConcurrentDictionary<(Guid User, string Asking), Lazy<Shelf>> _shelves = new();
    private readonly ConcurrentDictionary<Guid, List<Guid>> _started = new();
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
    /// How many shelves are standing. For the tests, and for anything that wants to know
    /// what this is holding.
    /// </summary>
    public int Standing => _shelves.Count;

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

        // Anything nobody has asked for since it went stale. Without this the dictionary
        // only ever grows: a shelf is dropped when it is asked for again, and an account
        // that opened the app once never asks again.
        Sweep();

        var key = (user, asking);

        while (true)
        {
            // Lazy rather than the value itself, so that several requests arriving together
            // wait on one build instead of each starting their own. The app asks for the
            // row and its first page at once, which is exactly that case.
            var wanted = new Lazy<Shelf>(() => new Shelf(_now(), build()));
            var standing = _shelves.GetOrAdd(key, wanted);

            Shelf shelf;

            try
            {
                shelf = standing.Value;
            }
            catch
            {
                // A Lazy remembers a failure as happily as a value, so a library that was
                // briefly unreadable would answer the same exception to every request for
                // the next ten minutes. Dropped, and the next request tries again.
                _shelves.TryRemove(new KeyValuePair<(Guid, string), Lazy<Shelf>>(key, standing));
                throw;
            }

            if (_now() - shelf.Built <= KeptFor)
            {
                return shelf.Items;
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

    /// <summary>
    /// Remembers that somebody has started watching something.
    /// </summary>
    /// <param name="user">Who started it.</param>
    /// <param name="item">What they started.</param>
    /// <remarks>
    /// Jellyfin does not call an item played until it ends, so what somebody is watching
    /// right now is in none of the queries a shelf is built from, and it is the best thing
    /// there is to go on. Remembered here rather than read back from the library, because
    /// an item started and abandoned at two minutes is not resumable either.
    /// </remarks>
    public void Started(Guid user, Guid item)
    {
        if (user.Equals(default) || item.Equals(default))
        {
            return;
        }

        _started.AddOrUpdate(
            user,
            _ => [item],
            (_, started) =>
            {
                lock (started)
                {
                    started.Remove(item);
                    started.Add(item);

                    while (started.Count > StartsKept)
                    {
                        started.RemoveAt(0);
                    }

                    return started;
                }
            });
    }

    /// <summary>
    /// What somebody has started watching lately, oldest first.
    /// </summary>
    /// <param name="user">Whose.</param>
    /// <returns>The ids, or nothing when they have started nothing.</returns>
    public IReadOnlyList<Guid> JustStarted(Guid user)
    {
        if (!_started.TryGetValue(user, out var started))
        {
            return [];
        }

        lock (started)
        {
            return [.. started];
        }
    }

    private void Sweep()
    {
        var now = _now();

        foreach (var (key, shelf) in _shelves)
        {
            // Never forces a build: one still being built is not stale, and asking a Lazy
            // for its value here would both block and run somebody else's build.
            if (shelf.IsValueCreated && now - shelf.Value.Built > KeptFor)
            {
                _shelves.TryRemove(new KeyValuePair<(Guid, string), Lazy<Shelf>>(key, shelf));
            }
        }
    }

    private sealed record Shelf(DateTime Built, List<Guid> Items);
}
