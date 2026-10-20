using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events.ItemAdded;

/// <summary>
/// Holds a new item message until Jellyfin knows the title's TMDB or TVDB id, for a while at
/// most (#289).
/// </summary>
/// <remarks>
/// The id is what says whether somebody waited for the title. A movie's message went out the
/// moment the movie was added, often before its metadata brought the id, so it could not leave
/// out the people the awaited title message told a moment later, and they got both.
/// </remarks>
internal sealed class IdentifiedTitles : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, List<Held>> _held = [];
    private readonly TimeSpan _longest;
    private readonly Action<Exception> _failed;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentifiedTitles"/> class.
    /// </summary>
    /// <param name="longest">How long a message waits for its title's id at most.</param>
    /// <param name="failed">Hears of a message that could not be sent.</param>
    public IdentifiedTitles(TimeSpan longest, Action<Exception> failed)
    {
        _longest = longest;
        _failed = failed;
    }

    /// <summary>
    /// Sends now when the title has its id, otherwise once an update brings it, or once the
    /// longest wait is over.
    /// </summary>
    /// <param name="title">The movie or the show the message is about.</param>
    /// <param name="send">Sends the message.</param>
    public void Send(BaseItem title, Action send)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(send);

        if (HasId(title))
        {
            Run(send);
            return;
        }

        var held = new Held(send);
        lock (_gate)
        {
            if (!_held.TryGetValue(title.Id, out var waiting))
            {
                waiting = [];
                _held[title.Id] = waiting;
            }

            waiting.Add(held);
            // Its callback takes the lock too, so it cannot run before the timer is kept.
            held.Timer = new Timer(_ => Expire(title.Id, held), null, _longest, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Sends what waits for an updated title, once the update brings its id.
    /// </summary>
    /// <param name="item">What Jellyfin updated.</param>
    public void Updated(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!HasId(item))
        {
            return;
        }

        List<Held>? released;
        lock (_gate)
        {
            if (!_held.Remove(item.Id, out released))
            {
                return;
            }
        }

        foreach (var held in released)
        {
            held.Timer?.Dispose();
            Run(held.Send);
        }
    }

    /// <summary>
    /// Drops everything held, sending nothing.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            foreach (var held in _held.Values.SelectMany(waiting => waiting))
            {
                held.Timer?.Dispose();
            }

            _held.Clear();
        }
    }

    /// <inheritdoc />
    public void Dispose() => Clear();

    /// <summary>
    /// Whether Jellyfin knows a title by its TMDB or TVDB id, the ids people wait by.
    /// </summary>
    /// <param name="title">The movie or the show.</param>
    /// <returns><c>true</c> when it has one.</returns>
    internal static bool HasId(BaseItem title) => AwaitedTitles.IdsOf(title.ProviderIds) is not (null, null);

    // Its id never came: sent anyway, to everyone the event goes to.
    private void Expire(Guid titleId, Held held)
    {
        lock (_gate)
        {
            if (!_held.TryGetValue(titleId, out var waiting) || !waiting.Remove(held))
            {
                return;
            }

            if (waiting.Count == 0)
            {
                _held.Remove(titleId);
            }
        }

        held.Timer?.Dispose();
        Run(held.Send);
    }

    // On a timer's thread or a library event's, where an exception nobody catches stops the
    // server or the scan.
    private void Run(Action send)
    {
        try
        {
            send();
        }
        catch (Exception e)
        {
            _failed(e);
        }
    }

    private sealed class Held(Action send)
    {
        public Action Send { get; } = send;

        public Timer? Timer { get; set; }
    }
}
