using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events.ItemAdded;

/// <summary>
/// Holds a new item message until what it waits for came, for a while at most: Jellyfin's name
/// for the title, and its TMDB or TVDB id where somebody may wait for the title (#289).
/// </summary>
/// <remarks>
/// The id is what says whether somebody waited for the title. A movie's message went out the
/// moment the movie was added, often before its metadata brought the id, so it could not leave
/// out the people the awaited title message told a moment later, and they got both.
///
/// <para>
/// The name is what the message says. Until its metadata names it, Jellyfin names a new title
/// after its folder, "Nosferatu (1922) [tmdbid-653]", and with the id in the folder name nothing
/// held the message for the refresh.
/// </para>
///
/// <para>
/// One timer for everything held, which sends what is due one message after the other: with a
/// timer per title, a scan adding two thousand movies without an id in their folder names sent
/// two thousand messages at once, on the threads the server answers requests with.
/// </para>
/// </remarks>
internal sealed class IdentifiedTitles : IDisposable
{
    // How long an update of a title is remembered, for a message about it held later.
    private static readonly TimeSpan KnownFor = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    // By title, for the update that brings what each waits for; by when the wait ends, for the timer.
    private readonly Dictionary<Guid, List<Held>> _byTitle = [];
    private readonly PriorityQueue<Held, TimeSpan> _byEnd = new();
    private readonly TimeProvider _time;
    private readonly long _origin;
    private readonly ITimer _timer;
    private readonly Action<Exception> _failed;
    private readonly Func<BaseItem, bool> _remember;
    // The titles to remember an update brought lately, by id, as the update left them.
    private readonly Dictionary<Guid, (BaseItem Title, TimeSpan At)> _known = [];
    private TimeSpan _sweptAt;
    private TimeSpan? _armedFor;
    // Once the server stopped, nothing is held: the timer would outlive it.
    private bool _stopped;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentifiedTitles"/> class.
    /// </summary>
    /// <param name="time">The clock the waits are counted on.</param>
    /// <param name="failed">Hears of a message that could not be sent.</param>
    /// <param name="remember">
    /// Which titles' updates are remembered for a while, so a message about one held later goes at
    /// once when the update already brought what it waits for: a batch of episodes can read a copy
    /// of its show from before the update that named it or brought its id, and wait for an update
    /// that already came. Nothing by default.
    /// </param>
    public IdentifiedTitles(TimeProvider time, Action<Exception> failed, Func<BaseItem, bool>? remember = null)
    {
        _time = time;
        _failed = failed;
        _remember = remember ?? (_ => false);
        // Counted from a timestamp, which a change of the server's clock does not move.
        _origin = time.GetTimestamp();
        _timer = time.CreateTimer(_ => SendDue(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Sends now when the title has its id, when its metadata already came without one, or when
    /// there is no wait; otherwise once an update brings the id or ends its refresh without one,
    /// or once the wait is over.
    /// </summary>
    /// <param name="title">The movie or the show the message is about.</param>
    /// <param name="wait">How long the message may wait for the id.</param>
    /// <param name="send">
    /// Sends the message about the title it is handed: the one the update brought, which
    /// carries the id, rather than the one added. Jellyfin keeps only so many items in memory
    /// and reads the others back from its database, so the one added may since have been read
    /// again without the id, while the update brings the scan's own, with it.
    /// </param>
    /// <returns>The send when it goes now, or a finished task when the message is held.</returns>
    public Task Send(BaseItem title, TimeSpan wait, Func<BaseItem, Task> send) => Send(title, wait, Identified, send);

    /// <summary>
    /// Sends now when the title already is as the message waits for it, or when there is no wait;
    /// otherwise once an update leaves it so, or once the wait is over.
    /// </summary>
    /// <param name="title">The movie or the show the message is about.</param>
    /// <param name="wait">How long the message may wait.</param>
    /// <param name="ready">Whether the message may go about the title as it is, added or updated.</param>
    /// <param name="send">
    /// Sends the message about the title it is handed: the one the update brought rather than the
    /// one added, as in the other overload.
    /// </param>
    /// <returns>The send when it goes now, or a finished task when the message is held.</returns>
    public Task Send(BaseItem title, TimeSpan wait, Func<BaseItem, bool> ready, Func<BaseItem, Task> send)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(ready);
        ArgumentNullException.ThrowIfNull(send);

        // Looked at under the lock only: an update that brought what it waits for between a look
        // outside it and the lock found nothing held, and the message waited the whole wait.
        var about = title;
        lock (_gate)
        {
            if (wait > TimeSpan.Zero && !_stopped && !ready(title))
            {
                // An update brought it already, on a copy fresher than this one.
                if (_known.TryGetValue(title.Id, out var known) && Now() - known.At <= KnownFor && ready(known.Title))
                {
                    about = known.Title;
                }
                else
                {
                    var held = new Held(title, ready, send, Now() + wait);
                    if (!_byTitle.TryGetValue(title.Id, out var waiting))
                    {
                        waiting = [];
                        _byTitle[title.Id] = waiting;
                    }

                    waiting.Add(held);
                    _byEnd.Enqueue(held, held.Ends);
                    Arm();
                    return Task.CompletedTask;
                }
            }
        }

        return Run(send, about);
    }

    /// <summary>
    /// Sends what waits for an updated title, each message once the update left the title as it
    /// waits for it.
    /// </summary>
    /// <param name="item">What Jellyfin updated.</param>
    public void Updated(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        List<Held> released;
        lock (_gate)
        {
            if (_remember(item))
            {
                Remember(item);
            }

            if (!_byTitle.TryGetValue(item.Id, out var waiting))
            {
                return;
            }

            released = waiting.FindAll(held => held.Ready(item));
            if (released.Count == 0)
            {
                return;
            }

            // The very ones released: asked again, a title whose ids Jellyfin writes meanwhile on
            // another thread could answer otherwise, and a message go twice or never.
            waiting.RemoveAll(released.Contains);
            if (waiting.Count == 0)
            {
                _byTitle.Remove(item.Id);
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
            _stopped = true;
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
    internal static bool HasId(BaseItem title) => AwaitedTitleAnnouncer.IdsOf(title) is not (null, null);

    /// <summary>
    /// Whether waiting for a title's id has come to an end: it has one, or its metadata came
    /// without one, which no id comes after.
    /// </summary>
    /// <param name="title">The movie or the show.</param>
    /// <returns><c>true</c> when holding its message would only make it late.</returns>
    /// <remarks>
    /// Without the second half, a show known by an AniDB id only, or a home video, waited the
    /// whole wait. Jellyfin stamps <c>DateLastRefreshed</c> once a refresh has asked every
    /// provider; it saves a new item once before that, unstamped, which ends nothing.
    /// </remarks>
    internal static bool Identified(BaseItem title) => HasId(title) || title.DateLastRefreshed != default;

    /// <summary>
    /// Whether Jellyfin has named a title since it was added: its metadata gave it another name
    /// than the one it had then, its folder's or its file's, or a refresh asked every provider,
    /// which no name comes after.
    /// </summary>
    /// <param name="title">The movie or the show, as it is now.</param>
    /// <param name="nameAtAdd">Its name when its message was held.</param>
    /// <returns><c>true</c> when holding its message for the name would only make it late.</returns>
    /// <remarks>
    /// A provider that fails leaves the stamp off, yet the name the others found is saved, and
    /// that save is an update too. Jellyfin saves a new item once before its refresh, under the
    /// name it was added with, which names nothing.
    /// </remarks>
    internal static bool Named(BaseItem title, string? nameAtAdd) =>
        title.DateLastRefreshed != default || !string.Equals(title.Name, nameAtAdd, StringComparison.Ordinal);

    private TimeSpan Now() => _time.GetElapsedTime(_origin);

    // Under the lock. Swept at most once a minute: a scan updates many titles in a row.
    private void Remember(BaseItem title)
    {
        var now = Now();
        _known[title.Id] = (title, now);

        if (now - _sweptAt < TimeSpan.FromMinutes(1))
        {
            return;
        }

        _sweptAt = now;
        foreach (var (id, known) in _known.ToList())
        {
            if (now - known.At > KnownFor)
            {
                _known.Remove(id);
            }
        }
    }

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

        return Detached.Observe(sending, _failed);
    }

    private sealed class Held(BaseItem title, Func<BaseItem, bool> ready, Func<BaseItem, Task> send, TimeSpan ends)
    {
        public BaseItem Title { get; } = title;

        public Func<BaseItem, bool> Ready { get; } = ready;

        public Func<BaseItem, Task> Send { get; } = send;

        public TimeSpan Ends { get; } = ends;

        public bool Sent { get; set; }
    }
}
