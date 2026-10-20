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
            failed: e => _logger.LogError(e, "Could not send a new item message"));
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
        seconds is { } set && double.IsFinite(set) && set >= 0
            ? TimeSpan.FromSeconds(Math.Min(set, ItemAddedNotificationConfiguration.LongestWaitForTitleId))
            : TimeSpan.FromSeconds(ItemAddedNotificationConfiguration.UsualWaitForTitleId);

    // How long a new item message about a title without its id waits for it (#289): only while
    // somebody waits for a title of its kind and the awaited title event can reach them. Then
    // the id says who to leave out; otherwise it would leave nobody out, and the message would
    // only be late.
    private TimeSpan IdWait(BaseItem title, string mediaType)
    {
        var wait = WaitForTitleId(Config?.notifications?.ItemAdded?.WaitForTitleId);

        if (wait == TimeSpan.Zero || IdentifiedTitles.HasId(title))
        {
            return TimeSpan.Zero;
        }

        try
        {
            return _notificationHelper.Wants(NotificationEvents.AwaitedTitle, Config?.notifications?.AwaitedTitle)
                && StreamyfinPlugin.Instance?.Database.AnybodyAwaits(mediaType) == true
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
    /// Everything a message about a season names, or <c>null</c> when the library no longer
    /// holds one of them.
    /// </summary>
    /// <param name="season">The season the message is about.</param>
    /// <param name="episodes">The episodes it counts.</param>
    /// <param name="lookup">How to read an item back, which answers null for one that is gone.</param>
    /// <returns>The season and its episodes, or <c>null</c>.</returns>
    /// <remarks>
    /// An episode that cannot be read cannot be checked against a user, and the message
    /// counts it all the same, so nothing goes out rather than a message authorized on less
    /// than it says.
    /// </remarks>
    internal static List<BaseItem>? EverythingNamed(
        BaseItem season,
        IEnumerable<Guid> episodes,
        Func<Guid, BaseItem?> lookup)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(lookup);

        List<BaseItem> named = [season];

        foreach (var id in episodes)
        {
            var episode = lookup(id);

            if (episode is null)
            {
                return null;
            }

            named.Add(episode);
        }

        return named;
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
                    callback: _ => OnEpisodeAddedTimerCallback(seasonId),
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
        var awaited = _announcer.Settle(movie, ToldSince(added));

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

    // An update can bring the id a held new item message waits for (#289).
    private void ItemUpdatedHandler(object? sender, ItemChangeEventArgs itemChangeEventArgs) =>
        _identified.Updated(itemChangeEventArgs.Item);

    private void OnEpisodeAddedTimerCallback(Guid seasonId)
    {
        _seasonItems.TryRemove(seasonId, out var countdown);
        _runs.Forget(DateTime.UtcNow);

        // A copy, since the scan may still be adding to the countdown while this reads it.
        var episodes = countdown?.Snapshot() ?? [];

        if (episodes.Count == 0) return;

        // A season whose series no longer resolves, deleted or identified again within the
        // minute, has nothing to name.
        if ((_libraryManager.GetItemById(seasonId) as Season)?.Series is not { } series)
        {
            _logger.LogInformation("The season {Season} has no series any more, so nothing was sent for its {Count} episode(s)", seasonId, episodes.Count);
            return;
        }

        _logger.LogInformation("Episode timer finished. Captured {0} episodes for {1}.", episodes.Count, series.Name.ForLog());

        // The people the awaited title message told about the show during this arrival are left
        // out of it: the show arriving is their one message, and a later arrival reaches them
        // like anyone (#289). Held until the show's id is known, like a movie.
        var since = ToldSince(countdown?.Began ?? DateTime.UtcNow);
        var ids = episodes.Select(added => added.Id).ToList();
        var first = episodes[0];

        _identified.Send(
            series,
            IdWait(series, AwaitedTitles.Tv),
            identified => SendEpisodes(seasonId, ids, first, identified, since));
    }

    // Everything is read back here, when the message goes, rather than when the batch closed:
    // held for the show's id, an episode may have gone meanwhile, and the message names how
    // many arrived and, for a single one, its number and its id.
    private Task SendEpisodes(Guid seasonId, List<Guid> episodes, Episode first, BaseItem series, DateTime since)
    {
        var total = episodes.Count;
        // Only logged: the message writes the name for itself, per language.
        var name = series.Name.ForLog();

        if (_libraryManager.GetItemById(seasonId) is not Season season)
        {
            _logger.LogInformation("The season {Season} is gone, so nothing was sent for its {Count} episode(s)", seasonId, total);
            return Task.CompletedTask;
        }

        // The one episode is read back here rather than inside the message, which is
        // written once per language and should not go to the library each time.
        Episode? single = null;

        if (total == 1)
        {
            single = _libraryManager.GetItemById(first.Id) as Episode;

            if (single is null)
            {
                return Task.CompletedTask;
            }
        }

        // The season and every episode the message counts. A season can be visible while an
        // episode in it is not.
        var named = EverythingNamed(season, episodes, id => _libraryManager.GetItemById(id));

        if (named is null)
        {
            _logger.LogInformation(
                "One of the {Count} episode(s) added to {Season} is no longer in the library, so nothing was sent",
                total,
                name);
            return Task.CompletedTask;
        }

        // The library for those who turned it off, the show for those who follow or muted it.
        var library = LibraryIdOf(_libraryManager.GetVirtualFolders(), first.Path);
        var subject = new NotificationSubject("itemAdded", library, series.Id);
        var awaited = _announcer.Settle(series, since);

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
        _seasonItems.Clear();

        // Sent rather than dropped: Jellyfin does not add a title again after a restart, so a
        // message held for its id would otherwise reach nobody (#289).
        try
        {
            await _identified.SendAll().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("The server stopped before every new item message held for its title's id was sent");
        }
    }

    /// <inheritdoc />
    public void Dispose() => _identified.Dispose();
}