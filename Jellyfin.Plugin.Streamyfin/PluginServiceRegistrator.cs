using System;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Streamyfin.Integrations;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Session;
using MediaBrowser.Controller.Library;
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
        // Helpers
        serviceCollection.AddSingleton<LocalizationHelper>();
        serviceCollection.AddSingleton<SerializationHelper>();
        serviceCollection.AddSingleton<NotificationHelper>();

        // The client that talks to Expo. Thirty seconds rather than the hundred an
        // HttpClient defaults to: a push send happens inside an event handler the server is
        // waiting on, so a hung request should give up long before that.
        serviceCollection
            .AddHttpClient(NotificationHelper.ExpoClientName, client => client.Timeout = TimeSpan.FromSeconds(30));

        serviceCollection.AddSingleton<IntegrationProbe>();

        // The client that reaches a third party integration. Eight seconds, the same as
        // the app's own probes: an administrator is watching a button, and a service that
        // has not answered in eight seconds is not one the app will wait for either.
        serviceCollection
            .AddHttpClient(IntegrationProbe.ClientName, client => client.Timeout = TimeSpan.FromSeconds(8));

        // Event listeners
        serviceCollection.AddScoped<IEventConsumer<SessionStartedEventArgs>, SessionStartEvent>();
        serviceCollection.AddScoped<IEventConsumer<PlaybackStartEventArgs>, PlaybackStartEvent>();
        serviceCollection.AddScoped<IEventConsumer<UserLockedOutEventArgs>, UserLockedOutEvent>();

        // Service
        serviceCollection.AddHostedService<ItemAddedService>();
    }
}