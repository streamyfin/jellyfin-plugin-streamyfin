using Jellyfin.Plugin.Streamyfin.PushNotifications;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Streamyfin;

/// <summary>
/// Provides service registration for the plugin
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<SerializationHelper>();
        serviceCollection.AddSingleton<NotificationHelper>();
    }
}