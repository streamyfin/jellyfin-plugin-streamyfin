using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
///
/// <para>
/// One timer for everything held, which sends what is due one message after the other: with a
/// timer per title, a scan adding two thousand movies without an id in their folder names sent
/// two thousand messages at once, on the threads the server answers requests with.
/// </para>
/// </remarks>
internal sealed class IdentifiedTitles : IDisposable
{
    private readonly object _gate = new();
    // By title, for the update that brings the id; by when the wait ends, for the timer.
    private readonly Dictionary<Guid, List<Held>> _byTitle = [];
    private readonly PriorityQueue<Held, TimeSpan> _byEnd = new();
    private readonly TimeProvider _time;
    private readonly long _origin;
    private readonly ITimer _timer;
    private readonly Action<Exception> _failed;
    private TimeSpan? _armedFor;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentifiedTitles"/> class.
    /// </summary>
    /// <param name="time">The clock the waits are counted on.</param>
    /// <param name="failed">Hears of a message that could not be sent.</param>
    public IdentifiedTitles(TimeProvider time, Action<Exception> failed)
    {
        _time = time;
        _failed = failed;
        // Counted from a timestamp, which a change of the server's clock does not move.
        _origin = time.GetTimestamp();
        _timer = time.CreateTimer(_ => SendDue(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Sends now when the title has its id, or when there is no wait; otherwise once an update
    /// brings the id, or once the wait is over.
    /// </summary>
    /// <param name="title">The movie or the show the message is about.</param>
    /// <param name="wait">How long the message may wait for the id.</param>
    /// <param name="send">
    /// Sends the message about the title it is handed: the one the update brought, which
    /// carries the id, rather than the one added. Jellyfin keeps only so many items in memory
    /// and reads the others back from its database, so the one added may since have been read
    /// again without the id, while the update brings the scan's own, with it.
    /// </param>
    public void Send(BaseItem title, TimeSpan wait, Func<BaseItem, Task> send)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(send);

        // Looked at under the lock only: an update that brought the id between a look outside
        // it and the lock found nothing held, and the message waited the whole wait.
        lock (_gate)
        {
            if (wait > TimeSpan.Zero && !HasId(title))
            {
                var held = new Held(title, send, Now() + wait);
                if (!_byTitle.TryGetValue(title.Id, out var waiting))
                {
                    waiting = [];
                    _byTitle[title.Id] = waiting;
                }

                waiting.Add(held);
                _byEnd.Enqueue(held, held.Ends);
                Arm();
                return;
            }
        }

        Run(send, title);
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
            if (!_byTitle.Remove(item.Id, out released))
            {
                return;
            }

            foreach (var held in released)
            {
                held.Sent = true;
            }
        }

        foreach (var held in released)
        {
            Run(held.Send, item);
        }
    }

    /// <summary>
    /// Sends everything held, at once, when the server stops.
    /// </summary>
    /// <returns>The sends, to wait for; it does not fail for one that does.</returns>
    /// <remarks>
    /// Kept in memory, a held message went with the server: a movie added within the wait before
    /// a restart was announced to nobody, since Jellyfin does not add it again. Sent without its
    /// id, it leaves nobody out, which is what it did before it was ever held.
    /// </remarks>
    public Task SendAll()
    {
        List<Held> all;
        lock (_gate)
        {
            all = [.. _byTitle.Values.SelectMany(waiting => waiting).OrderBy(held => held.Ends)];
            Forget();
        }

        return Task.WhenAll(all.Select(held => Run(held.Send, held.Title)));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            Forget();
        }

        _timer.Dispose();
    }

    /// <summary>
    /// Whether Jellyfin knows a title by its TMDB or TVDB id, the ids people wait by.
    /// </summary>
    /// <param name="title">The movie or the show.</param>
    /// <returns><c>true</c> when it has one.</returns>
    internal static bool HasId(BaseItem title) => AwaitedTitles.IdsOf(title.ProviderIds) is not (null, null);

    private TimeSpan Now() => _time.GetElapsedTime(_origin);

    // Under the lock.
    private void Forget()
    {
        foreach (var held in _byTitle.Values.SelectMany(waiting => waiting))
        {
            held.Sent = true;
        }

        _byTitle.Clear();
        _byEnd.Clear();
        _armedFor = null;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    // Their id never came: sent anyway, to everyone the event goes to, in the order their
    // waits ended.
    private void SendDue()
    {
        List<Held> due = [];
        lock (_gate)
        {
            _armedFor = null;
            var now = Now();

            while (_byEnd.TryPeek(out var next, out var ends) && (next.Sent || ends <= now))
            {
                _byEnd.Dequeue();
                if (next.Sent)
                {
                    continue;
                }

                next.Sent = true;
                var waiting = _byTitle[next.Title.Id];
                waiting.Remove(next);
                if (waiting.Count == 0)
                {
                    _byTitle.Remove(next.Title.Id);
                }

                due.Add(next);
            }

            Arm();
        }

        foreach (var held in due)
        {
            Run(held.Send, held.Title);
        }
    }

    // Under the lock: the timer wakes when the earliest wait still held ends.
    private void Arm()
    {
        while (_byEnd.TryPeek(out var head, out _) && head.Sent)
        {
            _byEnd.Dequeue();
        }

        if (!_byEnd.TryPeek(out _, out var ends) || _armedFor == ends)
        {
            return;
        }

        _armedFor = ends;
        var delay = ends - Now();
        _timer.Change(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    // On the timer's thread or a library event's, where an exception nobody catches stops the
    // server or the scan. The task it hands back never fails, for SendAll.
    private Task Run(Func<BaseItem, Task> send, BaseItem title)
    {
        Task sending;
        try
        {
            sending = send(title);
        }
        catch (Exception e)
        {
            _failed(e);
            return Task.CompletedTask;
        }

        return sending.ContinueWith(
            task =>
            {
                if (task.Exception is { } failure)
                {
                    _failed(failure.InnerExceptions.Count == 1 ? failure.InnerExceptions[0] : failure);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed class Held(BaseItem title, Func<BaseItem, Task> send, TimeSpan ends)
    {
        public BaseItem Title { get; } = title;

        public Func<BaseItem, Task> Send { get; } = send;

        public TimeSpan Ends { get; } = ends;

        public bool Sent { get; set; }
    }
}
