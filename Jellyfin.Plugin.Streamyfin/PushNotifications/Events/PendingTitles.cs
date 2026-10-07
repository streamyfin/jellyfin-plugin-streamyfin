using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;

/// <summary>
/// The titles added before they had an id to match, kept until their metadata brings one
/// (#225).
/// </summary>
/// <remarks>
/// An item only has its ids when it is added if the name of its folder carries them. The
/// others get them with their first metadata refresh, usually within the minute. Kept for a
/// while and no longer, so a title whose metadata never comes is not watched forever.
/// </remarks>
/// <param name="keep">How long a title is waited for.</param>
internal sealed class PendingTitles(TimeSpan keep)
{
    // A scan asks about every item it touches, so the list is swept at most this often and
    // an entry past its time is told apart by its own time in between.
    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, DateTime> _since = new();
    private long _sweptTicks = DateTime.MinValue.Ticks;

    /// <summary>Gets how many times the list was swept, for the tests.</summary>
    internal int Sweeps { get; private set; }

    /// <summary>Waits for a title's ids, from the first time it was seen without them.</summary>
    /// <param name="id">The movie or the show.</param>
    /// <param name="nowUtc">Now.</param>
    public void Add(Guid id, DateTime nowUtc)
    {
        Forget(nowUtc);
        _since.TryAdd(id, nowUtc);
    }

    /// <summary>Whether a title's ids are still waited for.</summary>
    /// <param name="id">The movie or the show.</param>
    /// <param name="nowUtc">Now.</param>
    /// <returns><c>true</c> while it is.</returns>
    public bool Has(Guid id, DateTime nowUtc)
    {
        Forget(nowUtc);
        return _since.TryGetValue(id, out var since) && nowUtc - since <= keep;
    }

    /// <summary>Stops waiting for a title's ids.</summary>
    /// <param name="id">The movie or the show.</param>
    /// <param name="nowUtc">Now.</param>
    /// <returns><c>true</c> when it was waited for.</returns>
    public bool Take(Guid id, DateTime nowUtc)
    {
        Forget(nowUtc);
        return _since.TryRemove(id, out var since) && nowUtc - since <= keep;
    }

    /// <summary>Forgets everything, when the server stops.</summary>
    public void Clear() => _since.Clear();

    private void Forget(DateTime nowUtc)
    {
        var last = Interlocked.Read(ref _sweptTicks);
        if (nowUtc.Ticks - last < SweepEvery.Ticks
            || Interlocked.CompareExchange(ref _sweptTicks, nowUtc.Ticks, last) != last)
        {
            return;
        }

        Sweeps++;
        foreach (var (id, since) in _since)
        {
            if (nowUtc - since > keep)
            {
                _since.TryRemove(id, out _);
            }
        }
    }
}
