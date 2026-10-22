using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.Db;
using Jellyfin.Plugin.Streamyfin.Extensions;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// Tells the people who waited for a title that it arrived (#225).
/// </summary>
/// <remarks>
/// Apart from the new items: those stop when an administrator turns new items off, and these
/// do not, since each one is a title somebody asked for by name. Every row is settled in the
/// database's write lock before anything is sent, so a title is announced once to each person.
///
/// <para>
/// The one owner of an arrival: the library's watcher and the new item messages both settle
/// through it, so a title is watched for its ids, settled and told the same way whichever
/// noticed it first (#289).
/// </para>
/// </remarks>
public class AwaitedTitleAnnouncer
{
    // The titles added before they had an id to match, until their metadata brings one.
    private readonly PendingTitles _pending = new(TimeSpan.FromHours(24));
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly NotificationHelper _notificationHelper;
    private readonly LocalizationHelper _localization;
    private readonly ILogger<AwaitedTitleAnnouncer> _logger;

    /// <summary>
    /// Initializes a new instance of the announcer.
    /// </summary>
    /// <param name="libraryManager">Reads back what a pause held.</param>
    /// <param name="userManager">Finds the people who waited.</param>
    /// <param name="notificationHelper">Sends to their devices.</param>
    /// <param name="localization">Writes the message in each device's language.</param>
    /// <param name="logger">The logger.</param>
    public AwaitedTitleAnnouncer(
        ILibraryManager libraryManager,
        IUserManager userManager,
        NotificationHelper notificationHelper,
        LocalizationHelper localization,
        ILogger<AwaitedTitleAnnouncer> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _notificationHelper = notificationHelper;
        _localization = localization;
        _logger = logger;
    }

    /// <summary>
    /// The title an added item makes available: a movie, or the show of an episode, since a
    /// show counts as arrived with its first episode.
    /// </summary>
    /// <param name="item">What was added.</param>
    /// <returns>The title, or <c>null</c> for anything else.</returns>
    internal static BaseItem? TitleOf(BaseItem item) => item switch
    {
        Movie { IsVirtualItem: false } movie => movie,
        Episode { IsVirtualItem: false } episode => episode.Series,
        _ => null
    };

    /// <summary>
    /// The TMDB and TVDB ids an item carries, read safely while Jellyfin may be writing them.
    /// </summary>
    /// <param name="item">The movie or the show.</param>
    /// <returns>Each id, or <c>null</c> when it is missing.</returns>
    /// <remarks>
    /// Jellyfin keeps an item's ids in a plain dictionary and writes them in place during a
    /// refresh, on its own thread, while a batch of episodes or a held message reads them on a
    /// timer's: a read that meets the write throws "Collection was modified". The write takes
    /// nanoseconds, so the read is simply made again.
    /// </remarks>
    internal static (int? Tmdb, int? Tvdb) IdsOf(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        for (var tried = 1; ; tried++)
        {
            try
            {
                return AwaitedTitles.IdsOf(item.ProviderIds);
            }
            // Whatever a read racing a write throws: the dictionary is not made for it.
            catch (Exception) when (tried < 10)
            {
                Thread.Yield();
            }
            catch (Exception)
            {
                return (null, null);
            }
        }
    }

    /// <summary>
    /// The earliest telling about another copy of a title that its new item message still
    /// leaves out: counted back from when the message's arrival began, not from when it goes,
    /// so a copy held for its id keeps the whole window.
    /// </summary>
    /// <param name="since">When the arrival began, a little before.</param>
    /// <param name="window">How long a copy leaves out the people told.</param>
    /// <returns>The moment, or none at all for a window of nothing, which leaves nobody out.</returns>
    internal static DateTime CopiesSince(DateTime since, TimeSpan window) =>
        window > TimeSpan.Zero ? since - window : DateTime.MaxValue;

    /// <summary>
    /// The title an update settles, when it is waited for: a show or a movie by its own update,
    /// whose metadata brings its ids, or a show by one of its episodes', which stood for the
    /// show when it was added.
    /// </summary>
    /// <param name="pending">The titles waited for.</param>
    /// <param name="updated">What was updated.</param>
    /// <param name="nowUtc">Now.</param>
    /// <returns>The title, or <c>null</c>.</returns>
    internal static BaseItem? SettledBy(PendingTitles pending, BaseItem updated, DateTime nowUtc)
    {
        // Nothing waited for, the usual case: a scan updates every item it touches, and an
        // episode's show is one more lookup each time.
        if (pending.IsEmpty)
        {
            return null;
        }

        var title = updated is Series ? updated : TitleOf(updated);
        return title is not null && pending.Has(title.Id, nowUtc) ? title : null;
    }

    /// <summary>
    /// The title an update settles, among the ones watched for their ids.
    /// </summary>
    /// <param name="updated">What was updated.</param>
    /// <param name="nowUtc">Now.</param>
    /// <returns>The title, or <c>null</c>.</returns>
    public BaseItem? SettledBy(BaseItem updated, DateTime nowUtc) => SettledBy(_pending, updated, nowUtc);

    /// <summary>
    /// Forgets the titles watched for their ids, when the server stops.
    /// </summary>
    public void ForgetPending() => _pending.Clear();

    /// <summary>
    /// Settles a title's arrival and tells the people it is for, in the background. A title
    /// without an id yet is watched until an update brings one.
    /// </summary>
    /// <param name="title">The movie or the show that arrived.</param>
    /// <remarks>Never throws: it runs inside the library's events, which must not fail the scan.</remarks>
    public void Consider(BaseItem title)
    {
        ArgumentNullException.ThrowIfNull(title);

        try
        {
            if (Arrive(title) is { } sending)
            {
                _pending.Take(title.Id, DateTime.UtcNow);
                // Settled already: a message Expo did not take is held again, and the task tells
                // them at its next run.
                _ = Detached.Observe(sending, e => _logger.LogError(e, "Failed to tell who waited for {Title} that it arrived", title.Name.ForLog()));
                return;
            }

            // No id yet: the metadata brings it.
            _pending.Add(title.Id, DateTime.UtcNow);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not settle the arrival of {Title}", title.Name.ForLog());
        }
    }

    /// <summary>
    /// Settles a title's arrival, telling the people it is for now, then answers who the new
    /// item message about it leaves out (#289).
    /// </summary>
    /// <param name="title">
    /// The movie or the show the message is about, as the update that brought its ids left it.
    /// </param>
    /// <param name="since">When the message's arrival began, a little before.</param>
    /// <param name="copiesWithin">How long a copy of the title leaves out the people told about it.</param>
    /// <returns>
    /// The people who waited for the title: told about this item since the arrival began, about
    /// another copy within the window before it, or held by a pause until it ends. Nobody when
    /// the title has no id, or the record cannot be read: the message then goes to everyone, as
    /// before.
    /// </returns>
    public IReadOnlySet<Guid> Settle(BaseItem title, DateTime since, TimeSpan copiesWithin)
    {
        ArgumentNullException.ThrowIfNull(title);

        // Settled here first, so the people told are known whichever library event noticed the
        // title first. Settling twice tells nobody twice.
        Consider(title);

        var (tmdb, tvdb) = IdsOf(title);
        var database = StreamyfinPlugin.Instance?.Database;

        if (database is null || (tmdb is null && tvdb is null))
        {
            return new HashSet<Guid>();
        }

        try
        {
            return database.WhoWaitedFor(
                title is Movie ? AwaitedTitles.Movie : AwaitedTitles.Tv,
                tmdb,
                tvdb,
                title.Id,
                since,
                CopiesSince(since, copiesWithin));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not read who waited for {Title}, so its new item message leaves nobody out", title.Name.ForLog());
            return new HashSet<Guid>();
        }
    }

    /// <summary>
    /// Settles a title's arrival for everyone who waited for it, and tells those it is for now.
    /// </summary>
    /// <param name="title">The movie or the show that arrived.</param>
    /// <returns>
    /// The send, or <c>null</c> when the title has no id yet and its metadata has to bring one.
    /// </returns>
    public Task? Arrive(BaseItem title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var database = StreamyfinPlugin.Instance?.Database;
        if (database is null)
        {
            return Task.CompletedTask;
        }

        var (tmdb, tvdb) = IdsOf(title);
        if (tmdb is null && tvdb is null)
        {
            return null;
        }

        // Built on the first waiting row only: most arrivals find nobody waiting.
        var decide = new Lazy<Func<AwaitedTitle, BaseItem, AwaitedOutcome>>(() => Deciding(database));
        var mediaType = title is Movie ? AwaitedTitles.Movie : AwaitedTitles.Tv;
        var told = database.SettleArrival(mediaType, tmdb, tvdb, title.Id, row => decide.Value(row, title));

        return told.Count == 0 ? Task.CompletedTask : Announce(database, title, told);
    }

    /// <summary>
    /// Tells the people whose pause held an arrival back, once the pause is over.
    /// </summary>
    /// <returns>How many were told.</returns>
    public async Task<int> SettleHeld()
    {
        var database = StreamyfinPlugin.Instance?.Database;
        if (database is null)
        {
            return 0;
        }

        var decide = Deciding(database);
        var titles = new Dictionary<Guid, BaseItem>();
        var told = database.SettleHeld(row =>
        {
            // Gone since it arrived: wait for it again.
            if (row.ArrivedItemId is not { } id || _libraryManager.GetItemById(id) is not { } title)
            {
                return AwaitedOutcome.KeepWaiting;
            }

            titles[id] = title;
            return decide(row, title);
        });

        await AnnounceEach(
            told.GroupBy(row => row.ArrivedItemId!.Value),
            arrival => Announce(database, titles[arrival.Key], [.. arrival]),
            (arrival, e) => _logger.LogError(e, "Could not announce {Title} after a pause", titles[arrival.Key].Name.ForLog()))
            .ConfigureAwait(false);

        return told.Count;
    }

    /// <summary>
    /// Forgets who was told about which title, once the new item messages about those titles
    /// are long sent (#289).
    /// </summary>
    /// <returns>How many records went.</returns>
    public int ForgetTold() =>
        StreamyfinPlugin.Instance?.Database.ForgetAnnouncedBefore(DateTime.UtcNow - AwaitedTitles.ToldKeptFor) ?? 0;

    /// <summary>
    /// Announces each arrival in turn, the next one even when one fails.
    /// </summary>
    /// <typeparam name="T">An arrival.</typeparam>
    /// <param name="arrivals">The arrivals, settled already.</param>
    /// <param name="announce">Sends one.</param>
    /// <param name="failed">Hears of one that could not be sent.</param>
    /// <returns>A task.</returns>
    /// <remarks>
    /// The rows are settled before anything is sent, so a failure that stopped the loop lost
    /// every arrival after it for good.
    /// </remarks>
    internal static async Task AnnounceEach<T>(IEnumerable<T> arrivals, Func<T, Task> announce, Action<T, Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(arrivals);
        ArgumentNullException.ThrowIfNull(announce);
        ArgumentNullException.ThrowIfNull(failed);

        foreach (var arrival in arrivals)
        {
            try
            {
                await announce(arrival).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                failed(arrival, e);
            }
        }
    }

    /// <summary>
    /// The message for one audience: the title with its year, opening its page.
    /// </summary>
    /// <param name="title">The movie or the show.</param>
    /// <param name="awaited">What the person asked for.</param>
    /// <param name="audience">The language and the address of the devices.</param>
    /// <returns>The message.</returns>
    internal ExpoNotificationRequest Message(BaseItem title, AwaitedTitle awaited, Audience audience)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(awaited);
        ArgumentNullException.ThrowIfNull(audience);

        // The title the person asked for, as Seerr named it. Until its metadata comes, Jellyfin
        // names an item by its folder, year and ids included.
        var name = awaited.Year is { } year
            ? _localization.GetFormatted(key: "NameAndYear", cultureInfo: audience.Culture, args: [awaited.Title.Escape(), year])
            : awaited.Title.Escape();

        return new ExpoNotificationRequest
        {
            Title = _localization.GetString("AwaitedTitleArrivedTitle", audience.Culture),
            Body = _localization.GetFormatted(key: "AwaitedTitleArrivedBody", cultureInfo: audience.Culture, args: [name]),
            // What the app opens: a movie's page, or a show's the way a batch of episodes
            // names it, which every app that opens notifications already knows.
            Data = title is Movie
                ? new Dictionary<string, object?> { ["id"] = title.Id.ToString("N"), ["type"] = "Movie" }
                : new Dictionary<string, object?> { ["seriesId"] = title.Id.ToString("N"), ["type"] = "Episode" },
            RichContent = DeviceServer.PosterOf(audience.ServerUrl, title.Id) is { } image
                ? new ExpoRichContent { Image = image }
                : null
        };
    }

    // One question per waiting row, with what every row shares read once.
    private Func<AwaitedTitle, BaseItem, AwaitedOutcome> Deciding(PluginDatabase database)
    {
        var targets = NotificationTargets.From(database);
        var preferences = database.AllNotificationPreferences();
        var serverOn = StreamyfinPlugin.Instance?.Settings.Current.notifications?.AwaitedTitle is { Enabled: true };
        var now = DateTime.UtcNow;

        return (row, title) =>
        {
            var user = _userManager.GetUserById(row.UserId);
            var exists = user is not null && !user.IsDisabled();
            var mine = preferences.GetValueOrDefault(row.UserId);

            return AwaitedTitles.Decide(
                personExists: exists,
                reaches: exists
                    && targets.Reaches(NotificationEvents.AwaitedTitle, row.UserId, serverOn, inDefaultAudience: true)
                    && (mine?.Keeps(NotificationEvents.AwaitedTitle) ?? true),
                canOpen: exists && title.IsVisibleStandalone(user!),
                paused: mine?.IsPaused(now) ?? false);
        };
    }

    /// <summary>
    /// The messages an arrival sends: one per title as the people asked for it, each to those
    /// people only.
    /// </summary>
    /// <param name="told">The rows to announce.</param>
    /// <returns>Each title as it was asked for, with who asked for it that way.</returns>
    /// <remarks>
    /// A row's title is text the person's own app sent. Named by one row and sent to everyone
    /// who waited, it let one person choose the words another received.
    /// </remarks>
    internal static IEnumerable<(AwaitedTitle Named, HashSet<Guid> People)> MessagesFor(IEnumerable<AwaitedTitle> told) =>
        told
            .GroupBy(row => (row.Title, row.Year))
            .Select(same => (same.First(), same.Select(row => row.UserId).ToHashSet()));

    /// <summary>
    /// Tells the people the rows name, one message per title as they asked for it.
    /// </summary>
    /// <param name="database">The rows' database.</param>
    /// <param name="title">The movie or the show that arrived.</param>
    /// <param name="told">The rows, settled as told.</param>
    /// <returns>A task, which does not fail for a message that never left.</returns>
    /// <remarks>
    /// The rows are kept as told before the message goes, and the new item message about the
    /// title leaves those people out. A message Expo did not take is put back as held for its
    /// own people only, so the task tells them at its next run rather than nobody telling them
    /// at all (#289). Only one Expo did not take: a message it may have sent, an error or a lost
    /// answer, would reach them twice, and Expo takes no key to tell the two apart.
    /// </remarks>
    internal async Task Announce(PluginDatabase database, BaseItem title, List<AwaitedTitle> told)
    {
        var devices = database.GetAllDeviceTokens();

        foreach (var (named, people) in MessagesFor(told))
        {
            _logger.LogInformation("{Title} arrived for {People} person(s) waiting for it", title.Name.ForLog(), people.Count);

            var theirs = devices.Where(device => people.Contains(device.UserId)).ToList();

            // No device to send to, now or later: nothing to try again.
            if (theirs.Count == 0)
            {
                _logger.LogInformation("Nobody waiting for {Title} has a device to tell", title.Name.ForLog());
                continue;
            }

            foreach (var (request, inIt) in ByRequest(theirs))
            {
                if (await NotTaken(request, title, named).ConfigureAwait(false) is not { } why)
                {
                    continue;
                }

                try
                {
                    var again = database.Untell(told.Where(row => inIt.Contains(row.UserId)));
                    _logger.LogWarning(
                        "Expo did not take the message telling {People} person(s) that {Title} arrived ({Why}): {Again} held again, told at the next run of the awaited titles task",
                        inIt.Count,
                        title.Name.ForLog(),
                        why,
                        again);
                }
                catch (Exception e)
                {
                    // The next request still goes.
                    _logger.LogError(e, "Could not hold the arrival of {Title} again for the people Expo did not tell", title.Name.ForLog());
                }
            }
        }
    }

    /// <summary>
    /// The devices of a message's people, a request's worth at a time, each person's devices in
    /// one request.
    /// </summary>
    /// <param name="devices">The devices.</param>
    /// <param name="limit">The most recipients Expo takes in one request.</param>
    /// <returns>Each request's devices, with the people they belong to.</returns>
    /// <remarks>
    /// Sent all at once, past a hundred devices, the message answered as soon as one request was
    /// taken: the people of a later request Expo turned away stayed told and heard nothing, and
    /// a later request that never reached Expo had everybody told again, those already told too.
    /// One request at a time, the one Expo did not take names exactly the people to tell again.
    /// </remarks>
    internal static IEnumerable<(List<DeviceToken> Devices, HashSet<Guid> People)> ByRequest(
        IEnumerable<DeviceToken> devices,
        int limit = ExpoBatching.MaxRecipientsPerRequest)
    {
        List<DeviceToken> request = [];
        HashSet<Guid> people = [];

        foreach (var person in devices.GroupBy(device => device.UserId))
        {
            var theirs = person.ToList();
            if (request.Count > 0 && request.Count + theirs.Count > limit)
            {
                yield return (request, people);
                request = [];
                people = [];
            }

            request.AddRange(theirs);
            people.Add(person.Key);
        }

        if (request.Count > 0)
        {
            yield return (request, people);
        }
    }

    // Sends one message, and says why when Expo did not take it: turned away as too many after
    // the retries it allows, or never reached. Nothing when it took it, or may have.
    private async Task<string?> NotTaken(List<DeviceToken> devices, BaseItem title, AwaitedTitle named)
    {
        HttpStatusCode? refused = null;

        try
        {
            var answer = await _notificationHelper.SendToDevices(
                devices,
                audience => [Message(title, named, audience)],
                new NotificationSubject(NotificationEvents.AwaitedTitle),
                status => refused = status).ConfigureAwait(false);

            if (answer is not null)
            {
                return null;
            }

            if (refused == HttpStatusCode.TooManyRequests)
            {
                return "too many requests";
            }

            _logger.LogError(
                "Expo refused the message telling who waited that {Title} arrived ({Status}); it is not sent again, since it may have gone out or would be refused again",
                title.Name.ForLog(),
                refused.HasValue ? (int)refused.Value : (object)"no answer");
            return null;
        }
        catch (HttpRequestException e) when (e.HttpRequestError is HttpRequestError.NameResolutionError
            or HttpRequestError.ConnectionError
            or HttpRequestError.SecureConnectionError
            or HttpRequestError.ProxyTunnelError)
        {
            _logger.LogWarning(e, "The message telling who waited that {Title} arrived never reached Expo", title.Name.ForLog());
            return "never reached";
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not tell who waited for {Title} that it arrived; it is not sent again, since it may have gone out", title.Name.ForLog());
            return null;
        }
    }
}
