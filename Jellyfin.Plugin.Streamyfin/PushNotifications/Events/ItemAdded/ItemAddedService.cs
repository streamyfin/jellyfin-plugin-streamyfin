using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.Extensions;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events.ItemAdded;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;


public class ItemAddedService : BaseEvent, IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ConcurrentDictionary<Guid, EpisodeTimer> _seasonItems;

    public ItemAddedService(ILibraryManager libraryManager,
        ILoggerFactory loggerFactory,
        LocalizationHelper localization,
        IServerApplicationHost applicationHost,
        NotificationHelper notificationHelper
    ) : base(loggerFactory, localization, applicationHost, notificationHelper)
    {
        _libraryManager = libraryManager;
        _seasonItems = new ConcurrentDictionary<Guid, EpisodeTimer>();
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
            Config?.notifications?.ItemAdded is not { Enabled: true }
        ) return;

        var item = itemChangeEventArgs.Item;
        var enabledLibraries = Config.notifications.ItemAdded.EnabledLibraries;
        var virtualFolder = _libraryManager.GetVirtualFolders()
            .Find(folder => folder.Locations.Any(location => item?.Path?.Contains(location, StringComparison.Ordinal) == true));

        if (virtualFolder != null && !IsLibraryEnabled(enabledLibraries, virtualFolder.ItemId))
        {
            _logger.LogInformation(
                "Failed to notify about item {0} - {1}. Library {2} currently not enabled for notifications.",
                item.GetType().Name, item.Name.Escape(), virtualFolder.Name
            );
            return;
        }

        _logger.LogInformation("Item added is {0} - {1}",  item.GetType().Name, item.Name.Escape());

        switch (item)
        {
            case Movie movie:
                var notification = MediaNotificationHelper.CreateMediaNotification(
                    localization: _localization,
                    title: _localization.GetFormatted("ItemAddedTitle", args: _localization.GetString("MovieMediaType")),
                    body: [],
                    item: item
                );

                if (notification != null)
                {
                    SendDetached(_notificationHelper.SendToWhoCanOpen(item, notification), "item added");
                }
                break;
            case Episode episode:
                var seasonId = episode.FindSeasonId();

                if (seasonId == Guid.Empty)
                {
                    return;
                }

                _seasonItems.TryGetValue(seasonId, out var _countdown);

                var countdown = _countdown ?? new EpisodeTimer(
                    episodes: [],
                    callback: _ => OnEpisodeAddedTimerCallback(seasonId)
                );

                countdown.Add(episode);
                _seasonItems.TryAdd(seasonId, countdown);
                break;
            default:
                _logger.LogInformation("Item type is not supported at this time. No notification will be sent out.");
                break;
        }
    }

    private void OnEpisodeAddedTimerCallback(Guid seasonId)
    {
        _seasonItems.TryRemove(seasonId, out var countdown);
        var total = countdown?.Episodes.Count ?? 0;

        if (countdown == null || countdown.Episodes.Count == 0) return;
        
        var episode = countdown.Episodes.First();
        var refreshedSeason = _libraryManager.GetItemById(seasonId) as Season;

        if (refreshedSeason is null)
        {
            return;
        }
        
        var name = refreshedSeason.Series.Name.Escape();

        string title;
        List<string> body = [];
        var data = new Dictionary<string, object?>();

        _logger.LogInformation("Episode timer finished. Captured {0} episodes for {1}.", total, name);

        if (total == 1)
        {
            var refreshedEpisode = _libraryManager.GetItemById(episode.Id) as Episode;
            if (refreshedEpisode is null)
            {
                return;
            }
            episode = refreshedEpisode;

            title = _localization.GetString("EpisodeAddedTitle");
            data["id"] = episode.Id.ToString("N"); // only provide for a single episode notification

            // Both episode & season information is available
            if (episode.IndexNumber != null && episode.Season.IndexNumber != null)
            {
                body.Add(
                    _localization.GetFormatted(
                        key: "EpisodeNumberAddedForSeason",
                        args: [name, episode.IndexNumber, episode.Season.IndexNumber]
                    )
                );
            }
            // only episode information is available
            else if (episode.IndexNumber != null)
            {
                body.Add(
                    _localization.GetFormatted(
                        key: "EpisodeAdded",
                        args: [name, episode.IndexNumber]
                    )
                );
            }
            // only season information is available
            else if (episode.Season.IndexNumber != null)
            {
                body.Add(
                    _localization.GetFormatted(
                        key: "EpisodeAddedForSeason",
                        args: [name, episode.Season.IndexNumber]
                    )
                );
            }
        }
        else
        {
            title = _localization.GetString("EpisodesAddedTitle");

            if (refreshedSeason.IndexNumber != null)
            {
                body.Add(_localization.GetFormatted(
                        key: "TotalEpisodesAddedForSeason",
                        args: [name, total, refreshedSeason.IndexNumber]
                    )
                );
            }
            else
            {
                body.Add(_localization.GetFormatted(
                        key: "EpisodesAddedToSeries",
                        args: [name, total]
                    )
                );
            }
        }

        data["seasonIndex"] = refreshedSeason.IndexNumber;
        data["seriesId"] = refreshedSeason.SeriesId.ToString("N");
        data["type"] = episode.GetType().Name.Escape();

        var notification = new ExpoNotificationRequest
        {
            Title = title,
            Body = string.Join("\n", body),
            Data = data
        };

        // The season and every episode the message counts. A season can be visible while an
        // episode in it is not, and this message names how many arrived and, for a single
        // one, its number and its id.
        var named = EverythingNamed(
            refreshedSeason,
            countdown.Episodes.Select(added => added.Id),
            id => _libraryManager.GetItemById(id));

        if (named is null)
        {
            _logger.LogInformation(
                "One of the {Count} episode(s) added to {Season} is no longer in the library, so nothing was sent",
                total,
                name);
            return;
        }

        // Observed like the movie send. Discarding the awaitable left a failure unobserved.
        SendDetached(_notificationHelper.SendToWhoCanOpen(named, notification), "episodes added");
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += ItemAddedHandler;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= ItemAddedHandler;
        _seasonItems.Clear();
        return Task.CompletedTask;
    }
}