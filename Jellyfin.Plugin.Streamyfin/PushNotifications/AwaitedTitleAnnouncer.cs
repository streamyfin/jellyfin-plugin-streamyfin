using System;
using System.Collections.Generic;
using System.Linq;
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
                // Settled already: a message that never left is held again, and the task tells
                // them at its next run.
                _ = sending.ContinueWith(
                    task => _logger.LogError(task.Exception, "Failed to tell who waited for {Title} that it arrived", title.Name.ForLog()),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
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
    /// <returns>
    /// The people who waited for the title: told about this item since the arrival began, about
    /// another copy within the hour, or held by a pause until it ends. Nobody when the title has
    /// no id, or the record cannot be read: the message then goes to everyone, as before.
    /// </returns>
    public IReadOnlySet<Guid> Settle(BaseItem title, DateTime since)
    {
        ArgumentNullException.ThrowIfNull(title);

        // Settled here first, so the people told are known whichever library event noticed the
        // title first. Settling twice tells nobody twice.
        Consider(title);

        var (tmdb, tvdb) = AwaitedTitles.IdsOf(title.ProviderIds);
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
                DateTime.UtcNow - AwaitedTitles.CopiesWithin);
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

        var (tmdb, tvdb) = AwaitedTitles.IdsOf(title.ProviderIds);
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
    /// title leaves those people out. A message Expo refused, or never answered, is put back as
    /// held for its own people only, so the task tells them at its next run rather than nobody
    /// telling them at all (#289).
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

            string? failure = null;

            try
            {
                var answer = await _notificationHelper.SendToDevices(
                    theirs,
                    audience => [Message(title, named, audience)],
                    new NotificationSubject(NotificationEvents.AwaitedTitle)).ConfigureAwait(false);

                if (answer is null)
                {
                    failure = "Expo did not take it";
                }
            }
            catch (Exception e)
            {
                failure = "it could not be sent";
                _logger.LogError(e, "Could not tell who waited for {Title} that it arrived", title.Name.ForLog());
            }

            if (failure is not null)
            {
                var again = database.Untell(told.Where(row => people.Contains(row.UserId)));
                _logger.LogWarning(
                    "Telling {People} person(s) that {Title} arrived failed ({Failure}): {Again} held again, told at the next run of the awaited titles task",
                    people.Count,
                    title.Name.ForLog(),
                    failure,
                    again);
            }
        }
    }
}
