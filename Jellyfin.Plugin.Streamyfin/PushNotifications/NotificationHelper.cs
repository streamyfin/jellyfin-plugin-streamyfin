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
    private readonly ILogger<NotificationHelper>? _logger;
    private readonly SerializationHelper _serializationHelper;
    private readonly IUserManager? _userManager;

    public NotificationHelper(
        ILoggerFactory? loggerFactory,
        IUserManager? userManager,
        SerializationHelper serializationHelper)
    {
        _logger = loggerFactory?.CreateLogger<NotificationHelper>();
        _userManager = userManager;
        _serializationHelper = serializationHelper;
    }

    /// <summary>
    /// Ability to send a batch of notifications directly to jellyfin admins
    /// </summary>
    /// <param name="notification"></param>
    /// <returns></returns>
    public async Task<ExpoNotificationResponse?> SendToAdmins(params Notification[] notifications)
    {
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
        using HttpClient client = new();
        var httpRequest = GetHttpRequestMessage(serializedRequest);
        var rawResponse = await client.SendAsync(httpRequest).ConfigureAwait(false);
        _logger?.LogDebug("Received response");
        httpRequest.Dispose();

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