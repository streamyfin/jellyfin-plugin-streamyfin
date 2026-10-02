using System;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Common.Plugins;
using Jellyfin.Plugin.Streamyfin.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Updates;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;

/// <summary>
/// Tells the administrators when a plugin is installed, updated or uninstalled.
/// </summary>
/// <remarks>
/// A server updates its plugins on its own, at every start and on a schedule, so this is
/// often the only place a change is announced to whoever runs it.
/// </remarks>
public class PluginChangedEvent(
    IPluginManager pluginManager,
    ILoggerFactory loggerFactory,
    LocalizationHelper localization,
    IServerApplicationHost applicationHost,
    NotificationHelper notificationHelper
) : BaseEvent(loggerFactory, localization, applicationHost, notificationHelper),
    IEventConsumer<PluginInstalledEventArgs>,
    IEventConsumer<PluginUpdatedEventArgs>,
    IEventConsumer<PluginUninstalledEventArgs>
{
    /// <summary>
    /// Whether a version arriving is an update or a first install.
    /// </summary>
    /// <param name="id">The plugin.</param>
    /// <param name="anotherVersionLoaded">Whether a version other than the arriving one is loaded.</param>
    /// <returns>What to call it.</returns>
    /// <remarks>
    /// Jellyfin publishes its updated event only when the version being installed is the
    /// one already there, which is a repair rather than an update, so a plugin going from
    /// 17 to 19 arrives as an install. The version it replaces stays loaded until the
    /// restart, beside the arriving one that the install adds straight away, and that is
    /// what tells the two apart.
    /// </remarks>
    public static PluginChange InstallOrUpdate(Guid id, Func<Guid, bool> anotherVersionLoaded)
    {
        ArgumentNullException.ThrowIfNull(anotherVersionLoaded);

        return anotherVersionLoaded(id) ? PluginChange.Updated : PluginChange.Installed;
    }

    /// <inheritdoc />
    public Task OnEvent(PluginInstalledEventArgs? eventArgs)
    {
        var arriving = eventArgs?.Argument;

        if (arriving is null)
        {
            return Task.CompletedTask;
        }

        var change = InstallOrUpdate(
            arriving.Id,
            id => pluginManager.Plugins.Any(loaded => loaded.Id.Equals(id) && !Equals(loaded.Version, arriving.Version)));

        return Tell(change, arriving.Name, arriving.Version?.ToString());
    }

    /// <inheritdoc />
    public Task OnEvent(PluginUpdatedEventArgs? eventArgs) =>
        Tell(PluginChange.Updated, eventArgs?.Argument?.Name, eventArgs?.Argument?.Version?.ToString());

    /// <inheritdoc />
    public Task OnEvent(PluginUninstalledEventArgs? eventArgs) =>
        Tell(PluginChange.Uninstalled, eventArgs?.Argument?.Name, eventArgs?.Argument?.Version?.ToString());

    /// <inheritdoc />
    protected override TimeSpan GetRecentEventThreshold() =>
        WaitFrom(Config?.notifications?.PluginChanged, base.GetRecentEventThreshold());

    private Task Tell(PluginChange change, string? name, string? version)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.CompletedTask;
        }

        if (Config?.notifications?.PluginChanged is not { Enabled: true })
        {
            _logger.LogInformation("{Plugin} was {Change}, and notifications about that are off", name, change);
            return Task.CompletedTask;
        }

        CleanupOldEntries();

        if (HasRecentlyProcessed($"plugin-changed:{change}:{name}:{version}"))
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("{Plugin} {Version} was {Change}, telling the administrators", name, version, change);

        SendDetached(
            _notificationHelper.SendForEvent(
                "pluginChanged",
                Config?.notifications?.PluginChanged,
                byDefault: user => user.IsAdministrator(),
                andAlso: null,
                write: audience => [AdminEvents.PluginChanged(_localization, change, name, version, audience.Culture)]),
            "plugin changed");

        return Task.CompletedTask;
    }
}
