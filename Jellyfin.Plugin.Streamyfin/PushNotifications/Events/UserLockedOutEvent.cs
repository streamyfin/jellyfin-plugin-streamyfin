using System;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Streamyfin.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Db;
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

        // The administrators and the account that was locked out, each device in the
        // language it asked for.
        var devices = _notificationHelper.AdminDevices()
            .Concat(StreamyfinPlugin.Instance?.Database.GetUserDeviceTokens(eventArgs.Argument.Id) ?? [])
            .ToList();

        await _notificationHelper.SendToDevices(
            devices,
            audience =>
            [
                new()
                {
                    Title = _localization.GetString("UserLockedOutTitle", audience.Culture),
                    Body = _localization.GetFormatted(
                        key: "UserHasBeenLockedOut",
                        cultureInfo: audience.Culture,
                        args: eventArgs.Argument.Username.Escape())
                }
            ]).ConfigureAwait(false);
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