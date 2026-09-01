using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.Extensions;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

public class NotificationHelper
{
    /// <summary>
    /// The name of the configured client that talks to Expo. Its timeout lives with the
    /// registration in <c>PluginServiceRegistrator</c> rather than at this call site.
    /// </summary>
    public const string ExpoClientName = "streamyfin-expo";

    private readonly ILogger<NotificationHelper>? _logger;
    private readonly SerializationHelper _serializationHelper;
    private readonly IUserManager? _userManager;
    private readonly IHttpClientFactory _httpClientFactory;

    public NotificationHelper(
        ILoggerFactory? loggerFactory,
        IUserManager? userManager,
        SerializationHelper serializationHelper,
        IHttpClientFactory httpClientFactory)
    {
        _logger = loggerFactory?.CreateLogger<NotificationHelper>();
        _userManager = userManager;
        _serializationHelper = serializationHelper;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Ability to send a batch of notifications directly to jellyfin admins
    /// </summary>
    /// <param name="notifications">The notifications to send.</param>
    /// <returns>Expo's response, or null when there is nobody to send to.</returns>
    public async Task<ExpoNotificationResponse?> SendToAdmins(params Notification[] notifications)
    {
        // Declared nullable and dereferenced all the same. A null here threw inside an event
        // handler the server was waiting on, rather than skipping a notification.
        if (_userManager is null)
        {
            _logger?.LogWarning("No user manager available, cannot work out which admins to notify");
            return null;
        }

        var adminTokens = _userManager.GetAdminTokens();

        _logger?.LogInformation("Attempting to send {0} notifications to admins", notifications.Length);

        // No admin tokens found.
        if (adminTokens.Count == 0)
        {
            _logger?.LogInformation("No admins found");
            return await Task.FromResult<ExpoNotificationResponse?>(null).ConfigureAwait(false);
        }

        var expoNotifications = notifications.Select(notification =>
        {
            List<String> userDeviceTokens = [];
            var expoNotification = notification.ToExpoNotification();
            
            // Also send to target user if specified
            if (notification.UserId.HasValue)
            {
                userDeviceTokens = StreamyfinPlugin.Instance?.Database
                    .GetUserDeviceTokens(notification.UserId.Value)
                    .Select(token => token.Token)
                    .ToList() ?? [];
            }

            expoNotification.To = adminTokens.Concat(userDeviceTokens).Distinct().ToList();
            return expoNotification;
        }).ToArray();

        return await Send(expoNotifications).ConfigureAwait(false);
    }

    public async Task<ExpoNotificationResponse?> SendToAll(params ExpoNotificationRequest[] notifications)
    {
        _logger?.LogInformation("Attempting to send {0} notifications to everyone", notifications.Length);

        var all = StreamyfinPlugin.Instance?.Database
            .GetAllDeviceTokens()
            .Select(token => token.Token)
            .Distinct()
            .ToList() ?? [];

        if (all.Count == 0)
        {
            _logger?.LogInformation("No devices found");
            return await Task.FromResult<ExpoNotificationResponse?>(null).ConfigureAwait(false);
        }
        
        var ready = notifications
            .Select(notification =>
            {
                notification.To = all;
                return notification;
            }).ToArray();
        
        return await Send(ready).ConfigureAwait(false);
    }

    public async Task<ExpoNotificationResponse?> SendToAdmins(
        List<Guid>? excludedUserIds = null,
        params ExpoNotificationRequest[] notifications)
    {
        _logger?.LogInformation("Attempting to send {0} notifications to admins", notifications.Length);

        if (_userManager is null)
        {
            _logger?.LogWarning("No user manager available, cannot work out which admins to notify");
            return null;
        }

        var excludedIds = excludedUserIds ?? Array.Empty<Guid>().ToList();
        var adminTokens = _userManager.GetAdminDeviceTokens()
            .FindAll(deviceToken => !excludedIds.Contains(deviceToken.UserId))
            .Select(deviceToken => deviceToken.Token)
            .Distinct()
            .ToList();

        // No admin tokens found.
        if (adminTokens.Count == 0)
        {
            _logger?.LogInformation("No admins found");
            return await Task.FromResult<ExpoNotificationResponse?>(null).ConfigureAwait(false);
        }

        var expoNotifications = notifications
            .Select(notification =>
            {
                notification.To = adminTokens;
                return notification;
            }).ToArray();

        return await Send(expoNotifications).ConfigureAwait(false);
    }

    public async Task<ExpoNotificationResponse?> Send(params ExpoNotificationRequest[] notifications) =>
        await SendNotificationToExpo(_serializationHelper.ToJson(notifications)).ConfigureAwait(false);

    private async Task<ExpoNotificationResponse?> SendNotificationToExpo(string serializedRequest)
    {
        _logger?.LogDebug("Preparing to send notification");

        // From the factory, never a new HttpClient per send: one built inline gets its own
        // connection pool every time and carries the default hundred second timeout, inside
        // an event handler that Jellyfin is waiting on.
        var client = _httpClientFactory.CreateClient(ExpoClientName);
        using var httpRequest = GetHttpRequestMessage(serializedRequest);
        using var rawResponse = await client.SendAsync(httpRequest).ConfigureAwait(false);

        // Expo answers 429 when it is being asked too often, and the body is then not a
        // ticket list. Read as one anyway it yields a response with no tickets, which every
        // caller reads as a delivery that simply had nothing to report.
        if (!rawResponse.IsSuccessStatusCode)
        {
            var body = await rawResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            _logger?.LogError(
                "Expo refused the notification with {Status}: {Body}",
                (int)rawResponse.StatusCode,
                body.Length > 500 ? body[..500] : body);

            return null;
        }

        _logger?.LogDebug("Received response");

        return await rawResponse.Content.ReadFromJsonAsync<ExpoNotificationResponse>().ConfigureAwait(false);
    }

    private static HttpRequestMessage GetHttpRequestMessage(string content) => new()
    {
        Method = HttpMethod.Post,
        RequestUri = new Uri("https://exp.host/--/api/v2/push/send"),
        Headers =
        {
            { "Host", "exp.host" },
            { "Accept", "application/json" },
            { "Accept-Encoding", "gzip, deflate" }
        },
        Content = new StringContent(
            content: content,
            encoding: Encoding.UTF8,
            mediaType: "application/json"
        )
    };
}