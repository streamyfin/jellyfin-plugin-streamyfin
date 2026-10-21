using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.Extensions;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events.ItemAdded;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;


public sealed class ItemAddedService : BaseEvent, IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly IShowWatching _watching;
    private readonly ConcurrentDictionary<Guid, EpisodeTimer> _seasonItems;
    private readonly AwaitedTitleAnnouncer _announcer;
    private readonly IdentifiedTitles _identified;
    private readonly ShowRuns _runs = new(EpisodeTimer.Gathers);

    public ItemAddedService(ILibraryManager libraryManager,
        ILoggerFactory loggerFactory,
        LocalizationHelper localization,
        IServerApplicationHost applicationHost,
        NotificationHelper notificationHelper,
        IUserDataManager userData,
        AwaitedTitleAnnouncer announcer
    ) : base(loggerFactory, localization, applicationHost, notificationHelper)
    {
        _libraryManager = libraryManager;
        _watching = new LibraryShowWatching(libraryManager, userData);
        _seasonItems = new ConcurrentDictionary<Guid, EpisodeTimer>();
        _announcer = announcer;
        _identified = new IdentifiedTitles(
            TimeProvider.System,
            failed: e => _logger.LogError(e, "Could not send a new item message"),
            // A show's batch closes a minute after its last episode, and can read a copy of the
            // show from before the update that identified it.
            remember: title => title is Series);
    }

    /// <summary>
    /// Who a new item message goes to: everyone it went to before but the people the awaited
    /// title message told about the same title, who get that one message instead (#289).
    /// </summary>
    /// <param name="told">The people told, by their Jellyfin user id.</param>
    /// <param name="canOpen">Who may open what the message names, which still decides.</param>
    /// <returns>Whether the message goes to a person.</returns>
    internal static Func<User, bool> LeaveOut(IReadOnlySet<Guid> told, Func<User, bool> canOpen) =>
        user => !told.Contains(user.Id) && canOpen(user);

    /// <summary>
    /// The earliest awaited title message that belongs to a new item message: a little before
    /// its arrival began, since the two notice the title in no set order, and after any arrival
    /// before it, which ended a batch's minute before this one began.
    /// </summary>
    /// <param name="began">When the movie came, or the run of episodes the batch came in began.</param>
    /// <returns>The moment.</returns>
    internal static DateTime ToldSince(DateTime began) => began - TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a new item message may wait for its title's id, as the administrator set it.
    /// </summary>
    /// <param name="seconds">What the configuration says, which may be nothing.</param>
    /// <returns>
    /// The wait: ten minutes when nothing is set, or when what is set is something the server
    /// could not count, which the page refuses but a configuration stored before it did may
    /// hold; an hour at most.
    /// </returns>
    internal static TimeSpan WaitForTitleId(double? seconds) =>
        Seconds(seconds, ItemAddedNotificationConfiguration.UsualWaitForTitleId, ItemAddedNotificationConfiguration.LongestWaitForTitleId);

    /// <summary>
    /// How long a copy of a title leaves out the people told about it, as the administrator set
    /// it.
    /// </summary>
    /// <param name="seconds">What the configuration says, which may be nothing.</param>
    /// <returns>
    /// The window: an hour when nothing is set, or when what is set is something the server
    /// could not count; a day at most, the day the record of who was told is kept.
    /// </returns>
    internal static TimeSpan CopiesWithin(double? seconds) =>
        Seconds(seconds, ItemAddedNotificationConfiguration.UsualCopiesWithin, ItemAddedNotificationConfiguration.LongestCopiesWithin);

    // A number of seconds as the administrator set it: the usual one when nothing is set, or when
    // what is set is something the server could not count, and never past the longest.
    private static TimeSpan Seconds(double? set, double usual, double longest) =>
        set is { } seconds && double.IsFinite(seconds) && seconds >= 0
            ? TimeSpan.FromSeconds(Math.Min(seconds, longest))
            : TimeSpan.FromSeconds(usual);

    private TimeSpan CopiesWithin() => CopiesWithin(Config?.notifications?.ItemAdded?.CopiesWithin);

    // How long a new item message about a title without its id waits for it (#289): only while
    // its metadata may still bring one, and somebody may be left out: a person who waits for a
    // title of its kind, whom a pause holds back, or who was told about one recently, whom a
    // copy would leave out. The awaited title event has to reach them too. Then the id says who
    // to leave out; otherwise it would leave nobody out, and the message would only be late:
    // Jellyfin refreshes a new title in a walk over its whole library, minutes on a large one.
    private TimeSpan IdWait(BaseItem title, string mediaType)
    {
        var wait = WaitForTitleId(Config?.notifications?.ItemAdded?.WaitForTitleId);

        if (wait == TimeSpan.Zero || IdentifiedTitles.Identified(title))
        {
            return TimeSpan.Zero;
        }

        try
        {
            return _notificationHelper.Wants(NotificationEvents.AwaitedTitle, Config?.notifications?.AwaitedTitle)
                && StreamyfinPlugin.Instance?.Database.MayLeaveSomebodyOut(mediaType, DateTime.UtcNow - CopiesWithin()) == true
                    ? wait
                    : TimeSpan.Zero;
        }
        catch (Exception e)
        {
            // Unable to tell, the message goes at once, as it always did.
            _logger.LogError(e, "Could not tell whether anybody waits for {Title}", title.Name.ForLog());
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Whether notifications are enabled for the library an item was added to.
    /// </summary>
    /// <param name="enabledLibraries">
    /// The configured library ids. Absent or empty means every library is enabled, so a
    /// configuration that never mentions <c>enabledLibraries</c> notifies for everything.
    /// </param>
    /// <param name="libraryItemId">The id of the library the item was found in.</param>
    /// <returns><c>true</c> when the library should produce notifications.</returns>
    public static bool IsLibraryEnabled(string[]? enabledLibraries, string? libraryItemId)
    {
        if (enabledLibraries is not { Length: > 0 }) return true;

        return libraryItemId is not null
               && enabledLibraries.Contains(libraryItemId, StringComparer.Ordinal);
    }

    /// <summary>
    /// The library an item went into, found by its path.
    /// </summary>
    /// <param name="folders">The server's libraries.</param>
    /// <param name="path">Where the item is.</param>
    /// <returns>The library, or <c>null</c> when no library holds that path.</returns>
    /// <remarks>
    /// A library holds a path when one of its folders is a whole leading part of it, so
    /// <c>/media/movies</c> does not hold <c>/media/movies-old</c>. A folder written exactly
    /// as the path comes first, then one that differs only by case, which Windows paths may;
    /// among those, the nearer one wins.
    /// </remarks>
    internal static VirtualFolderInfo? FolderOf(IEnumerable<VirtualFolderInfo> folders, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return folders
            .SelectMany(folder => (folder.Locations ?? []).Select(location => (Folder: folder, Location: location.TrimEnd('/', '\\'))))
            .Where(candidate => candidate.Location.Length > 0 && Holds(candidate.Location, path, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => Holds(candidate.Location, path, StringComparison.Ordinal))
            .ThenByDescending(candidate => candidate.Location.Length)
            .Select(candidate => candidate.Folder)
            .FirstOrDefault();

        static bool Holds(string location, string path, StringComparison comparison) =>
            path.StartsWith(location, comparison)
            && (path.Length == location.Length || path[location.Length] is '/' or '\\');
    }

    /// <summary>
    /// The id of the library an item went into, found by its path.
    /// </summary>
    /// <param name="folders">The server's libraries.</param>
    /// <param name="path">Where the item is.</param>
    /// <returns>The library id, or <c>null</c> when no library holds that path.</returns>
    internal static Guid? LibraryIdOf(IEnumerable<VirtualFolderInfo> folders, string? path) =>
        Guid.TryParse(FolderOf(folders, path)?.ItemId, out var id) ? id : null;

    // Written once per language among the devices it goes to, so it builds rather than
    // hands back what it built before.
    private ExpoNotificationRequest[] MovieMessage(BaseItem item, Audience audience)
    {
        var message = MediaNotificationHelper.CreateMediaNotification(
            localization: _localization,
            title: _localization.GetFormatted("ItemAddedTitle", audience.Culture, _localization.GetString("MovieMediaType", audience.Culture)),
            body: [],
            item: item,
            culture: audience.Culture);

        if (message is null)
        {
            return [];
        }

        message.RichContent = Poster(audience, item.Id);

        return [message];
    }

    // What the notification shows beside its text, fetched from the address that device
    // reaches the server at. A device that named no address gets no image rather than one
    // it cannot fetch.
    private ExpoRichContent? Poster(Audience audience, Guid itemId)
    {
        var image = DeviceServer.PosterOf(audience.ServerUrl, itemId);

        // At debug, since it is the answer to the only question this part ever raises:
        // why a notification arrived without its image.
        _logger.LogDebug("Poster for {Item}: {Image}", itemId, image ?? "none, the device named no server");

        return image is null ? null : new ExpoRichContent { Image = image };
    }

    /// <summary>
    /// What a message about a season still names: the season and the episodes the library still
    /// holds, or <c>null</c> when it holds none of them.
    /// </summary>
    /// <param name="season">The season the message is about.</param>
    /// <param name="episodes">The episodes it gathered.</param>
    /// <param name="lookup">How to read an item back, which answers null for one that is gone.</param>
    /// <returns>Everything the message names, to check against a user, and the episodes it counts.</returns>
    /// <remarks>
    /// An episode gone since it came, deleted or replaced by a new file, is neither counted nor
    /// checked against anybody, so the message never names more than it was authorized on, and
    /// the others are still announced. A batch held for its show's id made that likelier: a
    /// repack of one episode, which Jellyfin removes and adds again, used to keep every other
    /// from being announced (#289).
    /// </remarks>
    internal static (List<BaseItem> Named, List<Guid> Episodes)? StillThere(
        BaseItem season,
        IEnumerable<Guid> episodes,
        Func<Guid, BaseItem?> lookup)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(lookup);

        List<BaseItem> named = [season];
        List<Guid> there = [];

        foreach (var id in episodes)
        {
            if (lookup(id) is { } episode)
            {
                named.Add(episode);
                there.Add(id);
            }
        }

        return there.Count == 0 ? null : (named, there);
    }

    private void ItemAddedHandler(object? sender, ItemChangeEventArgs itemChangeEventArgs)
    {
        if (
            itemChangeEventArgs.Item.IsVirtualItem || 
            itemChangeEventArgs.Item.IsFolder || 
            !_notificationHelper.Wants("itemAdded", Config?.notifications?.ItemAdded)
        ) return;

        var item = itemChangeEventArgs.Item;
        // Read through: with targeting the event can run while the server itself has it
        // off, so nothing here may assume the block exists. An absent list means every
        // library, which is what IsLibraryEnabled already answers.
        var enabledLibraries = Config?.notifications?.ItemAdded?.EnabledLibraries;
        var virtualFolder = FolderOf(_libraryManager.GetVirtualFolders(), item.Path);

        if (virtualFolder != null && !IsLibraryEnabled(enabledLibraries, virtualFolder.ItemId))
        {
            _logger.LogInformation(
                "Failed to notify about item {0} - {1}. Library {2} currently not enabled for notifications.",
                item.GetType().Name, item.Name.ForLog(), virtualFolder.Name.ForLog()
            );
            return;
        }

        _logger.LogInformation("Item added is {0} - {1}",  item.GetType().Name, item.Name.ForLog());

        // Which library it went into, for the people who turned that library off.
        Guid? library = Guid.TryParse(virtualFolder?.ItemId, out var found) ? found : null;

        switch (item)
        {
            case Movie movie:
                var added = DateTime.UtcNow;
                // Held until Jellyfin knows the movie's id, which says who waited for it (#289).
                _identified.Send(
                    movie,
                    IdWait(movie, AwaitedTitles.Movie),
                    identified => SendMovie(movie.Id, identified, added, library));
                break;
            case Episode episode:
                var seasonId = episode.FindSeasonId();

                if (seasonId == Guid.Empty)
                {
                    return;
                }

                // Counted before the batch: a season the scan reaches minutes into a show's
                // arrival still belongs to it (#289).
                var began = _runs.Add(episode.FindSeriesId(), DateTime.UtcNow);

                _seasonItems.TryGetValue(seasonId, out var _countdown);

                var countdown = _countdown ?? new EpisodeTimer(
                    episodes: [],
                    callback: _ => Gathered(seasonId, stopping: false),
                    // On the timer's thread, where an exception nobody catches stops the server.
                    failed: e => _logger.LogError(e, "Could not announce the episodes added to season {Season}", seasonId)
                )
                {
                    Began = began
                };

                countdown.Add(episode);
                _seasonItems.TryAdd(seasonId, countdown);
                break;
            default:
                _logger.LogInformation("Item type is not supported at this time. No notification will be sent out.");
                break;
        }
    }

    private Task SendMovie(Guid movieId, BaseItem movie, DateTime added, Guid? library)
    {
        // Read back: held until its id came, the movie may have gone meanwhile. What the message
        // says comes from the one handed in, which the update that brought the id left with
        // its metadata: the library may hand back a copy it read again before, without them.
        if (_libraryManager.GetItemById(movieId) is not Movie)
        {
            return Task.CompletedTask;
        }

        // The people who waited for it get the awaited title message instead, settled here
        // (#289).
        var awaited = _announcer.Settle(movie, ToldSince(added), CopiesWithin());

        return _notificationHelper.SendForEvent(
            "itemAdded",
            Config?.notifications?.ItemAdded,
            byDefault: _ => true,
            // Not negotiable, whatever a level says: a title only goes to
            // somebody who may open it (#69).
            andAlso: LeaveOut(awaited, NotificationHelper.CanOpenEvery([movie])),
            write: audience => MovieMessage(movie, audience),
            subject: new NotificationSubject("itemAdded", library));
    }

    // An update can bring the id a held new item message waits for (#289). Only a movie's or a
    // show's: a scan updates every item it touches, and those are the titles messages wait on.
    private void ItemUpdatedHandler(object? sender, ItemChangeEventArgs itemChangeEventArgs)
    {
        if (itemChangeEventArgs.Item is Movie or Series)
        {
            _identified.Updated(itemChangeEventArgs.Item);
        }
    }

    /// <summary>
    /// Sends what the server would otherwise lose as it stops: the batches still gathering, then
    /// the messages held for their title's id.
    /// </summary>
    /// <param name="closing">Each batch still gathering, sent when called.</param>
    /// <param name="held">Sends the messages held.</param>
    /// <param name="failed">Hears of one that could not be sent.</param>
    /// <returns>Every send, to wait for.</returns>
    /// <remarks>
    /// One batch whose season cannot be read, the library closing under it, keeps neither the
    /// others nor the held messages from going.
    /// </remarks>
    internal static Task SendEverything(IEnumerable<Func<Task>> closing, Func<Task> held, Action<Exception> failed)
    {
        List<Task> sending = [];

        foreach (var close in closing)
        {
            try
            {
                sending.Add(close());
            }
            catch (Exception e)
            {
                failed(e);
            }
        }

        try
        {
            sending.Add(held());
        }
        catch (Exception e)
        {
            failed(e);
        }

        return Task.WhenAll(sending);
    }

    // A batch closed: a minute without another episode of its season, or the server stopping,
    // which sends it at once rather than lose it with the countdown.
    private Task Gathered(Guid seasonId, bool stopping)
    {
        if (!_seasonItems.TryRemove(seasonId, out var countdown))
        {
            return Task.CompletedTask;
        }

        _runs.Forget(DateTime.UtcNow);

        // A copy, since the scan may still be adding to the countdown while this reads it.
        var episodes = countdown.Snapshot();

        if (episodes.Count == 0) return Task.CompletedTask;

        // A season whose series no longer resolves, deleted or identified again within the
        // minute, has nothing to name.
        if ((_libraryManager.GetItemById(seasonId) as Season)?.Series is not { } series)
        {
            _logger.LogInformation("The season {Season} has no series any more, so nothing was sent for its {Count} episode(s)", seasonId, episodes.Count);
            return Task.CompletedTask;
        }

        _logger.LogInformation("Episode timer finished. Captured {0} episodes for {1}.", episodes.Count, series.Name.ForLog());

        // The people the awaited title message told about the show during this arrival are left
        // out of it: the show arriving is their one message, and a later arrival reaches them
        // like anyone (#289). Held until the show's id is known, like a movie.
        var since = ToldSince(countdown.Began);
        var ids = episodes.Select(added => added.Id).ToList();
        var first = episodes[0];

        return _identified.Send(
            series,
            stopping ? TimeSpan.Zero : IdWait(series, AwaitedTitles.Tv),
            identified => SendEpisodes(seasonId, ids, first, identified, since));
    }

    // Everything is read back here, when the message goes, rather than when the batch closed:
    // held for the show's id, an episode may have gone meanwhile, and the message names how
    // many arrived and, for a single one, its number and its id.
    private Task SendEpisodes(Guid seasonId, List<Guid> gathered, Episode first, BaseItem series, DateTime since)
    {
        // Only logged: the message writes the name for itself, per language.
        var name = series.Name.ForLog();

        if (_libraryManager.GetItemById(seasonId) is not Season season)
        {
            _logger.LogInformation("The season {Season} is gone, so nothing was sent for its {Count} episode(s)", seasonId, gathered.Count);
            return Task.CompletedTask;
        }

        // The season and every episode the message counts. A season can be visible while an
        // episode in it is not.
        if (StillThere(season, gathered, id => _libraryManager.GetItemById(id)) is not { } still)
        {
            _logger.LogInformation("None of the {Count} episode(s) added to {Season} is in the library any more, so nothing was sent", gathered.Count, name);
            return Task.CompletedTask;
        }

        var (named, episodes) = still;

        if (episodes.Count < gathered.Count)
        {
            _logger.LogInformation("{Gone} of the {Count} episode(s) added to {Season} are no longer in the library, so the message counts the others", gathered.Count - episodes.Count, gathered.Count, name);
        }

        var total = episodes.Count;

        // The one episode, read back with the rest, names its number and its id.
        var single = total == 1 ? named[1] as Episode : null;

        if (total == 1 && single is null)
        {
            return Task.CompletedTask;
        }

        // The library for those who turned it off, the show for those who follow or muted it.
        var library = LibraryIdOf(_libraryManager.GetVirtualFolders(), first.Path);
        var subject = new NotificationSubject("itemAdded", library, series.Id);
        var awaited = _announcer.Settle(series, since, CopiesWithin());

        return _notificationHelper.SendForEvent(
            "itemAdded",
            Config?.notifications?.ItemAdded,
            byDefault: _ => true,
            andAlso: LeaveOut(awaited, NotificationHelper.CanOpenEvery(named)),
            write: audience => [EpisodesMessage(season, series, single, first, total, audience)],
            subject: subject,
            watching: _watching);
    }

    // Written once per language among the devices it goes to. The show is the one handed in,
    // named by its metadata, rather than the season's, which may be a copy read before it.
    private ExpoNotificationRequest EpisodesMessage(
        Season season,
        BaseItem series,
        Episode? single,
        Episode first,
        int total,
        Audience audience)
    {
        var culture = audience.Culture;

        var name = series.Name.Escape();
        var data = new Dictionary<string, object?>();
        List<string> body = [];
        string title;

        if (single is not null)
        {
            title = _localization.GetString("EpisodeAddedTitle", culture);
            data["id"] = single.Id.ToString("N"); // only provide for a single episode notification

            // Both episode & season information is available
            if (single.IndexNumber != null && single.Season.IndexNumber != null)
            {
                body.Add(_localization.GetFormatted(
                    key: "EpisodeNumberAddedForSeason",
                    cultureInfo: culture,
                    args: [name, single.IndexNumber, single.Season.IndexNumber]));
            }
            // only episode information is available
            else if (single.IndexNumber != null)
            {
                body.Add(_localization.GetFormatted(
                    key: "EpisodeAdded",
                    cultureInfo: culture,
                    args: [name, single.IndexNumber]));
            }
            // only season information is available
            else if (single.Season.IndexNumber != null)
            {
                body.Add(_localization.GetFormatted(
                    key: "EpisodeAddedForSeason",
                    cultureInfo: culture,
                    args: [name, single.Season.IndexNumber]));
            }
        }
        else
        {
            title = _localization.GetString("EpisodesAddedTitle", culture);

            body.Add(season.IndexNumber != null
                ? _localization.GetFormatted(
                    key: "TotalEpisodesAddedForSeason",
                    cultureInfo: culture,
                    args: [name, total, season.IndexNumber])
                : _localization.GetFormatted(
                    key: "EpisodesAddedToSeries",
                    cultureInfo: culture,
                    args: [name, total]));
        }

        data["seasonIndex"] = season.IndexNumber;
        data["seriesId"] = series.Id.ToString("N");
        data["type"] = first.GetType().Name.Escape();

        return new ExpoNotificationRequest
        {
            Title = title,
            Body = string.Join("\n", body),
            Data = data,

            // The series rather than the episode: a poster is what a phone shows well, and
            // an episode's own image is a frame of it.
            RichContent = Poster(audience, series.Id)
        };
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += ItemAddedHandler;
        _libraryManager.ItemUpdated += ItemUpdatedHandler;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= ItemAddedHandler;
        _libraryManager.ItemUpdated -= ItemUpdatedHandler;

        // Sent rather than dropped, the batches still gathering and the messages held for their
        // title's id: Jellyfin does not add a title again after a restart, so they would
        // otherwise reach nobody (#289).
        var sending = SendEverything(
            [.. _seasonItems.Keys.ToList().Select<Guid, Func<Task>>(seasonId => () => Gathered(seasonId, stopping: true))],
            _identified.SendAll,
            e => _logger.LogError(e, "Could not send a new item message as the server stopped"));

        try
        {
            await sending.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("The server stopped before every new item message held back was sent");
        }
    }

    /// <inheritdoc />
    public void Dispose() => _identified.Dispose();
}