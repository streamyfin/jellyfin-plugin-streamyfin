using System;
using System.Collections.Generic;
using System.Threading;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events.ItemAdded;

/// <summary>
/// Gathers the episodes of a season added within a minute of each other, so that one
/// notification names them all.
/// </summary>
/// <remarks>
/// The callback runs on a <see cref="Timer"/>, where an exception nobody catches ends the whole
/// server's process, so it is caught here and handed to <c>failed</c>. The episodes are read by
/// the callback while the library scan may still be adding to them, so both go through a lock
/// and the callback reads a copy.
/// </remarks>
public class EpisodeTimer
{
    private readonly List<Episode> _episodes;
    private readonly TimerCallback _callback;
    private readonly Action<Exception> _failed;
    private readonly object _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="EpisodeTimer"/> class.
    /// </summary>
    /// <param name="episodes">The episodes to start from.</param>
    /// <param name="callback">What to do once no episode arrived for a minute.</param>
    /// <param name="failed">What to do with an exception the callback throws.</param>
    public EpisodeTimer(List<Episode> episodes, TimerCallback callback, Action<Exception> failed)
    {
        _episodes = episodes;
        _callback = callback;
        _failed = failed;
        Timer = new Timer(Fire, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    // Held the way it always was: it fires once, and goes with the countdown.
    private Timer Timer { get; }

    /// <summary>
    /// Gets when the first episode of the batch came, which says which awaited title messages
    /// were about this arrival (#289).
    /// </summary>
    public DateTime Opened { get; } = DateTime.UtcNow;

    /// <summary>
    /// Adds an episode, or replaces the one with its id, and starts the minute again.
    /// </summary>
    /// <param name="episode">The episode added.</param>
    public void Add(Episode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);

        lock (_gate)
        {
            ResetTimer();
            var index = _episodes.FindIndex(e => e.Id == episode.Id);

            if (index == -1)
            {
                _episodes.Add(episode);
            }
            else
            {
                _episodes[index] = episode;
            }
        }
    }

    /// <summary>
    /// The episodes gathered so far, as a copy, so reading them never races an addition.
    /// </summary>
    /// <returns>The episodes, in the order they arrived.</returns>
    public IReadOnlyList<Episode> Snapshot()
    {
        lock (_gate)
        {
            return [.. _episodes];
        }
    }

    /// <summary>
    /// Runs the callback, the way the timer does, handing an exception to <c>failed</c>.
    /// </summary>
    /// <param name="state">The timer's state.</param>
    internal void Fire(object? state)
    {
        try
        {
            _callback(state);
        }
        catch (Exception e)
        {
            _failed(e);
        }
    }

    private void ResetTimer() => Timer.Change(TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
}
