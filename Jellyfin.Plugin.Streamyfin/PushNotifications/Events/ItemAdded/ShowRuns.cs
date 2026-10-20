using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events.ItemAdded;

/// <summary>
/// When each show's run of new episodes began: the episodes of a show that come less than a
/// batch's minute apart arrive together, whichever season they are in (#289).
/// </summary>
/// <remarks>
/// A show that arrives with several seasons is one batch per season, each opened as the scan
/// reaches it. The awaited title message is about the show and goes with its first episode,
/// so the people it told are left out of every batch of that arrival, not only of the ones
/// that opened within half a minute of it.
/// </remarks>
/// <param name="gap">The longest pause between two episodes of one run.</param>
internal sealed class ShowRuns(TimeSpan gap)
{
    private readonly ConcurrentDictionary<Guid, (DateTime Began, DateTime Last)> _runs = new();

    /// <summary>Gets how many runs are kept, for the tests.</summary>
    internal int Count => _runs.Count;

    /// <summary>
    /// Counts an episode of a show, and answers when the run it belongs to began.
    /// </summary>
    /// <param name="show">The show.</param>
    /// <param name="nowUtc">When the episode came.</param>
    /// <returns>When its run began.</returns>
    public DateTime Add(Guid show, DateTime nowUtc) =>
        _runs.AddOrUpdate(
            show,
            _ => (nowUtc, nowUtc),
            (_, run) => nowUtc - run.Last <= gap ? (run.Began, nowUtc) : (nowUtc, nowUtc)).Began;

    /// <summary>
    /// Forgets the runs that ended: the next episode of those shows begins a run of its own.
    /// </summary>
    /// <param name="nowUtc">Now.</param>
    public void Forget(DateTime nowUtc)
    {
        foreach (var run in _runs)
        {
            // Only if it did not go on meanwhile.
            if (nowUtc - run.Value.Last > gap)
            {
                _runs.TryRemove(new KeyValuePair<Guid, (DateTime, DateTime)>(run.Key, run.Value));
            }
        }
    }
}
