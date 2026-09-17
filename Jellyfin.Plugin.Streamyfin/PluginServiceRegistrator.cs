using System;
using System.Net.Http;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Streamyfin.Integrations;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
using Jellyfin.Plugin.Streamyfin.Recommendations;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Events.Session;
using MediaBrowser.Controller.Events.Updates;
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
        serviceCollection.AddSingleton<SeerrNotificationMapper>();

        // The client that talks to Expo. Thirty seconds rather than the hundred an
        // HttpClient defaults to: a push send happens inside an event handler the server is
        // waiting on, so a hung request should give up long before that.
        serviceCollection
            .AddHttpClient(NotificationHelper.ExpoClientName, client => client.Timeout = TimeSpan.FromSeconds(30));

        serviceCollection.AddSingleton<IntegrationProbe>();

        // One per server rather than one per request: a "for you" row costs a scan of
        // everything unwatched in the genres somebody watches, and the app asks for it a
        // page at a time.
        serviceCollection.AddSingleton<ForYouShelves>();

        // The client that reaches a third party integration. Eight seconds, the same as
        // the app's own probes: an administrator is watching a button, and a service that
        // has not answered in eight seconds is not one the app will wait for either.
        serviceCollection
            .AddHttpClient(IntegrationProbe.ClientName, client => client.Timeout = TimeSpan.FromSeconds(8))
            // A probe reports on the address that was typed, so it follows nothing and
            // remembers nothing: a redirect would report on somewhere else, and a cookie
            // from one probe would change the answer to the next.
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            });

        // Event listeners
        serviceCollection.AddScoped<IEventConsumer<SessionStartedEventArgs>, SessionStartEvent>();
        serviceCollection.AddScoped<IEventConsumer<PlaybackStartEventArgs>, PlaybackStartEvent>();
        serviceCollection.AddScoped<IEventConsumer<UserLockedOutEventArgs>, UserLockedOutEvent>();
        serviceCollection.AddScoped<IEventConsumer<PluginInstalledEventArgs>, PluginChangedEvent>();
        serviceCollection.AddScoped<IEventConsumer<PluginUpdatedEventArgs>, PluginChangedEvent>();
        serviceCollection.AddScoped<IEventConsumer<PluginUninstalledEventArgs>, PluginChangedEvent>();
        serviceCollection.AddScoped<IEventConsumer<AuthenticationRequestEventArgs>, SignInFailedEvent>();

        // A second consumer of the same event, kept apart from the notification: the row
        // is thrown away whether or not an administrator wants to hear about playback.
        serviceCollection.AddScoped<IEventConsumer<PlaybackStartEventArgs>, ForYouShelfInvalidator>();

        // Service
        serviceCollection.AddHostedService<ItemAddedService>();

        // A scheduled task that fails is not published through the event manager, on either
        // Jellyfin line, so this one listens to the task manager itself.
        serviceCollection.AddHostedService<TaskFailedService>();
    }
}