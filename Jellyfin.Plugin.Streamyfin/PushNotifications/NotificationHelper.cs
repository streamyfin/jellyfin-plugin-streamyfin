using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
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

    private const string SendUri = "https://exp.host/--/api/v2/push/send";

    private const string ReceiptsUri = "https://exp.host/--/api/v2/push/getReceipts";

    /// <summary>
    /// How many ticket ids Expo takes in one receipts request.
    /// </summary>
    public const int MaxReceiptsPerRequest = 1000;

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

    public async Task<ExpoNotificationResponse?> Send(params ExpoNotificationRequest[] notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        // The order Expo answers in. One ticket comes back per recipient, and that
        // position is the only thing tying an error ticket to the device it came from.
        var recipients = notifications.SelectMany(notification => notification.To).ToList();

        // No token to pass: a send happens inside a synchronous Jellyfin event handler,
        // which has none to give. The client timeout is what bounds it.
        var response = await PostToExpo<ExpoNotificationResponse>(
            SendUri,
            _serializationHelper.ToJson(notifications),
            CancellationToken.None).ConfigureAwait(false);

        PruneAndQueue(recipients, response);

        return response;
    }

    /// <summary>
    /// Asks Expo what became of pushes it accepted earlier.
    /// </summary>
    /// <param name="ticketIds">The ticket ids to ask about, at most a thousand.</param>
    /// <param name="cancellationToken">Stops the call when the server is shutting down.</param>
    /// <returns>The receipts, or null when the request was refused.</returns>
    /// <exception cref="ArgumentOutOfRangeException">More ids than Expo takes at once.</exception>
    /// <remarks>
    /// A ticket only says Expo took the message. Whether it arrived, and above all
    /// whether the device is gone, is only ever in the receipt. Nothing called this
    /// before P4.2, so a token stayed in the database after its app was uninstalled and
    /// every later send to it went nowhere.
    /// </remarks>
    public async Task<ExpoReceiptResponse?> FetchReceipts(
        IReadOnlyList<string> ticketIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticketIds);

        if (ticketIds.Count == 0)
        {
            return null;
        }

        // Expo rejects the request past its cap, and a rejection is not an answer, so the
        // rows would simply be asked about again every hour until they expired unread.
        // Louder than a caller quietly never collecting anything.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ticketIds.Count, MaxReceiptsPerRequest);

        return await PostToExpo<ExpoReceiptResponse>(
            ReceiptsUri,
            _serializationHelper.ToJson(new ExpoReceiptRequest { Ids = [.. ticketIds] }),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Acts on what a send answer said about its recipients.
    /// </summary>
    /// <remarks>
    /// Deliberately silent when there is no plugin instance, which is the case in a unit
    /// test: the decision itself is in <see cref="ExpoTickets"/> and is tested there,
    /// without a database.
    /// </remarks>
    private void PruneAndQueue(IReadOnlyList<string> recipients, ExpoNotificationResponse? response)
    {
        var outcome = ExpoTickets.Reconcile(recipients, response, _logger);

        var database = StreamyfinPlugin.Instance?.Database;
        if (database is null)
        {
            return;
        }

        if (outcome.DeadTokens.Count > 0)
        {
            var removed = database.RemoveDeviceTokensNamed(outcome.DeadTokens);

            _logger?.LogInformation(
                "Expo reported {Devices} device(s) as no longer registered, {Rows} token row(s) removed",
                outcome.DeadTokens.Count,
                removed);
        }

        if (outcome.Pending.Count > 0)
        {
            database.AddExpoReceipts(
                outcome.Pending.Select(pending => (pending.TicketId, pending.Token)),
                DateTime.UtcNow);
        }
    }

    private async Task<T?> PostToExpo<T>(
        string uri,
        string serializedRequest,
        CancellationToken cancellationToken)
        where T : class
    {
        _logger?.LogDebug("Preparing to call {Uri}", uri);

        // From the factory, never a new HttpClient per send: one built inline gets its own
        // connection pool every time and carries the default hundred second timeout, inside
        // an event handler that Jellyfin is waiting on.
        var client = _httpClientFactory.CreateClient(ExpoClientName);
        using var httpRequest = GetHttpRequestMessage(uri, serializedRequest);
        using var rawResponse = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        // Expo answers 429 when it is being asked too often, and the body is then not a
        // ticket list. Read as one anyway it yields a response with no tickets, which every
        // caller reads as a delivery that simply had nothing to report.
        if (!rawResponse.IsSuccessStatusCode)
        {
            var body = await rawResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogError(
                "Expo refused the request to {Uri} with {Status}: {Body}",
                uri,
                (int)rawResponse.StatusCode,
                body.Length > 500 ? body[..500] : body);

            return null;
        }

        _logger?.LogDebug("Received response");

        return await rawResponse.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage GetHttpRequestMessage(string uri, string content) => new()
    {
        Method = HttpMethod.Post,
        RequestUri = new Uri(uri),
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