using System;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;

/// <summary>
/// Tells the administrators when the server refuses a sign in.
/// </summary>
/// <remarks>
/// Jellyfin publishes this for a name it does not know and for a password that does not
/// match, and locks an account out after a few of the second kind, which is an event of
/// its own here. The notification carries the name that was tried and the address it came
/// from, so it goes to administrators only, as the rest of these do.
/// </remarks>
public class SignInFailedEvent(
    ILoggerFactory loggerFactory,
    LocalizationHelper localization,
    IServerApplicationHost applicationHost,
    NotificationHelper notificationHelper
) : BaseEvent(loggerFactory, localization, applicationHost, notificationHelper), IEventConsumer<AuthenticationRequestEventArgs>
{
    /// <summary>
    /// How long two refusals from the same place wait by default. A server anyone can
    /// reach is tried by machines that never stop, and a notification for each of those is
    /// a reason to turn the whole thing off.
    /// </summary>
    private static readonly TimeSpan Wait = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public Task OnEvent(AuthenticationRequestEventArgs? eventArgs)
    {
        if (eventArgs is null)
        {
            return Task.CompletedTask;
        }

        if (Config?.notifications?.SignInFailed is not { Enabled: true })
        {
            _logger.LogInformation("A sign in was refused, and notifications about that are off");
            return Task.CompletedTask;
        }

        CleanupOldEntries();

        // Per address rather than per name: a machine working through a list of names is
        // one thing happening, and telling an administrator fifty times says no more than
        // telling them once.
        var key = string.IsNullOrWhiteSpace(eventArgs.RemoteEndPoint)
            ? $"sign-in-failed:{eventArgs.Username}"
            : $"sign-in-failed:{eventArgs.RemoteEndPoint}";

        if (HasRecentlyProcessed(key))
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("A sign in was refused, telling the administrators");

        SendDetached(
            _notificationHelper.SendToAdmins(
                excludedUserIds: null,
                write: audience => [AdminEvents.SignInFailed(_localization, eventArgs.Username, eventArgs.RemoteEndPoint, audience.Culture)]),
            "sign in failed");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override TimeSpan GetRecentEventThreshold() =>
        WaitFrom(Config?.notifications?.SignInFailed, Wait);
}
