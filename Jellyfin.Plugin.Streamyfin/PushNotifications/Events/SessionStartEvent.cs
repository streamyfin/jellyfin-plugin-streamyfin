using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using Jellyfin.Plugin.Streamyfin.Extensions;
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
        if (eventArgs?.Argument == null
            || !_notificationHelper.Wants("sessionStarted", Config?.notifications?.SessionStarted))
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

        SendDetached(
            _notificationHelper.SendForEvent(
                "sessionStarted",
                Config?.notifications?.SessionStarted,
                byDefault: user => user.IsAdministrator(),
                // Nobody is told about their own session, however they were targeted: it
                // is not news to the person who just signed in.
                andAlso: user => !user.Id.Equals(eventArgs.Argument.UserId),
                write: audience =>
                [
                    new()
                    {
                        Title = _localization.GetString("SessionStartTitle", audience.Culture),
                        Body = _localization.GetFormatted("UserNowOnline", audience.Culture, eventArgs.Argument.UserName)
                    }
                ]
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