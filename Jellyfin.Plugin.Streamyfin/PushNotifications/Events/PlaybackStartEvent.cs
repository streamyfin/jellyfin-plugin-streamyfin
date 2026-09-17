using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.Extensions;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;

/// <summary>
/// Session start notifier.
/// </summary>
public class PlaybackStartEvent(
    ILoggerFactory loggerFactory,
    LocalizationHelper localization,
    IServerApplicationHost applicationHost,
    NotificationHelper notificationHelper
) : BaseEvent(loggerFactory, localization, applicationHost, notificationHelper), IEventConsumer<PlaybackStartEventArgs>
{

    /// <inheritdoc />
    public async Task OnEvent(PlaybackStartEventArgs? eventArgs)
    {
        if (eventArgs == null || Config?.notifications?.PlaybackStarted is not { Enabled: true })
        {
            _logger.LogInformation("PlaybackStartEvent received but currently disabled.");
            return;
        }

        if (eventArgs.Item is null)
        {
            return;
        }

        if (eventArgs.Item.IsThemeMedia)
        {
            // Don't report theme song or local trailer playback.
            return;
        }

        if (eventArgs.Users.Count == 0)
        {
            // No users in playback session.
            return;
        }
        _logger.LogInformation("PlaybackStartEvent received.");

        CleanupOldEntries();

        // Who is watching, decided before anything is written: the same playback is one
        // event whatever language the message ends up in, and the wait is on the pair of
        // the item and the user rather than on the sentence, which now varies.
        var watching = eventArgs.Users
            .Where(user => !HasRecentlyProcessed($"playback:{eventArgs.Item.Id}:{user.Id}"))
            .ToList();

        if (watching.Count > 0)
        {
            SendDetached(
                _notificationHelper.SendToAdmins(
                    excludedUserIds: eventArgs.Users.Select(u => u.Id).ToList(),
                    write: culture => watching
                        .Select(user =>
                            MediaNotificationHelper.CreateMediaNotification(
                                localization: _localization,
                                title: _localization.GetString("PlaybackStartTitle", culture),
                                body: [_localization.GetFormatted("UserWatching", culture, user.Username)],
                                item: eventArgs.Item,
                                culture: culture
                            )
                        )
                        .OfType<ExpoNotificationRequest>()
                        .ToArray()
                ),
                "playback started");
        }
        else _logger.LogInformation("There are no valid notifications to send.");
    }

    /// <inheritdoc />
    protected override TimeSpan GetRecentEventThreshold()
    {
        if (Config?.notifications?.PlaybackStarted is { RecentEventThreshold: null })
            return base.GetRecentEventThreshold();

        var definedThreshold = (double) Config?.notifications?.PlaybackStarted?.RecentEventThreshold!;
        return TimeSpan.FromSeconds(double.Abs(definedThreshold));
    }
}