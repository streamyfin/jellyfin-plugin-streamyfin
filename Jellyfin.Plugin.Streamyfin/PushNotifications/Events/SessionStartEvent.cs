using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;

/// <summary>
/// Session start notifier.
/// </summary>
public class SessionStartEvent(
    ILoggerFactory loggerFactory,
    LocalizationHelper localization,
    IServerApplicationHost applicationHost,
    NotificationHelper notificationHelper
) : BaseEvent(loggerFactory, localization, applicationHost, notificationHelper), IEventConsumer<SessionStartedEventArgs>
{
    /// <inheritdoc />
    public async Task OnEvent(SessionStartedEventArgs? eventArgs)
    {
        if (eventArgs?.Argument == null || Config?.notifications?.SessionStarted is not { Enabled: true })
        {
            _logger.LogInformation("SessionStartEvent received but currently disabled.");
            return;
        }

        // Clean up old session entries when a new session event is triggered
        CleanupOldEntries();

        // Prevent the same notification per device
        string sessionKey = eventArgs.Argument.DeviceId;

        // Check if we've processed a similar event recently
        if (HasRecentlyProcessed(sessionKey))
        {
            return;
        }

        ExpoNotificationRequest[] notifications = [
            new()
            {
                Title = _localization.GetString("SessionStartTitle"),
                Body = _localization.GetFormatted("UserNowOnline", args: eventArgs.Argument.UserName)
            }
        ];

        SendDetached(
            _notificationHelper.SendToAdmins(
                excludedUserIds: [eventArgs.Argument.UserId],
                notifications: notifications
            ),
            "session started");
    }

    /// <inheritdoc />
    protected override TimeSpan GetRecentEventThreshold()
    {
        if (Config?.notifications?.SessionStarted is { RecentEventThreshold: null })
            return base.GetRecentEventThreshold();

        var definedThreshold = (double) Config?.notifications?.SessionStarted?.RecentEventThreshold!;
        return TimeSpan.FromSeconds(double.Abs(definedThreshold));
    }
}