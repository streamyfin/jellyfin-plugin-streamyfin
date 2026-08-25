using System;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Streamyfin.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;

/// <summary>
/// Session start notifier.
/// </summary>
public class UserLockedOutEvent(
    ILoggerFactory loggerFactory,
    LocalizationHelper localization,
    IServerApplicationHost applicationHost,
    NotificationHelper notificationHelper
) : BaseEvent(loggerFactory, localization, applicationHost, notificationHelper), IEventConsumer<UserLockedOutEventArgs>
{
    /// <inheritdoc />
    public async Task OnEvent(UserLockedOutEventArgs? eventArgs)
    {
        if (eventArgs?.Argument == null || Config?.notifications?.UserLockedOut is not { Enabled: true })
        {
            _logger.LogInformation("UserLockedOutEvent received but currently disabled.");
            return;
        }

        var notification = new Notification
        {
            Title = _localization.GetString("UserLockedOutTitle"),
            Body = _localization.GetFormatted(
                    key: "UserHasBeenLockedOut",
                    args: eventArgs.Argument.Username.Escape()
                ),
            UserId = eventArgs.Argument.Id
        };

        await _notificationHelper.SendToAdmins(notification).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override TimeSpan GetRecentEventThreshold()
    {
        if (Config?.notifications?.UserLockedOut is { RecentEventThreshold: null })
            return base.GetRecentEventThreshold();

        var definedThreshold = (double) Config?.notifications?.UserLockedOut?.RecentEventThreshold!;
        return TimeSpan.FromSeconds(double.Abs(definedThreshold));
    }
}