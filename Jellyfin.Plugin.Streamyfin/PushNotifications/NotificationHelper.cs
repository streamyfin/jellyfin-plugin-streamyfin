using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Streamyfin.Db;
using Jellyfin.Plugin.Streamyfin.Extensions;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using MediaBrowser.Controller.Entities;
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
    private readonly ExpoRetry _retry;
    private readonly ExpoRetry _retryReads;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationHelper"/> class.
    /// </summary>
    /// <param name="loggerFactory">Where the sends are reported.</param>
    /// <param name="userManager">Jellyfin's users, for the admin recipients.</param>
    /// <param name="serializationHelper">The plugin's serializer.</param>
    /// <param name="httpClientFactory">The factory holding the configured Expo client.</param>
    /// <param name="retry">
    /// When a refused request is worth making again. Defaults to
    /// <see cref="ExpoRetry.Default"/>; a test passes <see cref="ExpoRetry.Immediate"/>
    /// rather than sleeping through its own retries.
    /// </param>
    public NotificationHelper(
        ILoggerFactory? loggerFactory,
        IUserManager? userManager,
        SerializationHelper serializationHelper,
        IHttpClientFactory httpClientFactory,
        ExpoRetry? retry = null)
    {
        _logger = loggerFactory?.CreateLogger<NotificationHelper>();
        _userManager = userManager;
        _serializationHelper = serializationHelper;
        _httpClientFactory = httpClientFactory;
        _retry = retry ?? ExpoRetry.Default;
        _retryReads = _retry.ForSomethingThatOnlyReads();
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

    /// <summary>
    /// The devices of the users who may be told about something, each once.
    /// </summary>
    /// <param name="tokens">The registered devices.</param>
    /// <param name="mayKnow">Whether a user may be told, by user id. Asked once per user.</param>
    /// <returns>The tokens to send to, in the order the devices were given.</returns>
    /// <remarks>
    /// A token is taken to belong to the user on its row. The database keeps that true by
    /// leaving each token on one row, the device that registered it last.
    /// </remarks>
    internal static List<string> RecipientsWho(IEnumerable<DeviceToken> tokens, Func<Guid, bool> mayKnow)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(mayKnow);

        var answers = new Dictionary<Guid, bool>();

        bool Allowed(Guid user)
        {
            if (!answers.TryGetValue(user, out var allowed))
            {
                allowed = mayKnow(user);
                answers[user] = allowed;
            }

            return allowed;
        }

        return tokens
            .Where(token => Allowed(token.UserId))
            .Select(token => token.Token)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Whether an account may be told about something it can open.
    /// </summary>
    /// <param name="user">The account, or <c>null</c> when it is gone.</param>
    /// <param name="canOpen">Whether the account may open the thing.</param>
    /// <returns>True when the account exists, is not disabled, and may open it.</returns>
    /// <remarks>
    /// Jellyfin refuses every request a disabled account makes, but its devices are still
    /// registered here, so without this it went on hearing about new items.
    /// </remarks>
    internal static bool MayBeTold(User? user, Func<User, bool> canOpen)
    {
        ArgumentNullException.ThrowIfNull(canOpen);

        return user is not null && !user.IsDisabled() && canOpen(user);
    }

    /// <summary>
    /// Whether a user may be told about every one of these items.
    /// </summary>
    /// <param name="items">Everything the message describes.</param>
    /// <returns>The question to ask about a user.</returns>
    /// <remarks>
    /// A message about a batch names more than the one item it was authorized on. A season
    /// can be visible while an episode in it is not, since an episode carries its own
    /// rating and its own tags and <c>IsVisibleStandalone</c> looks at the item it is given
    /// and its parents, never at its children.
    /// </remarks>
    internal static Func<User, bool> CanOpenEvery(IReadOnlyCollection<BaseItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return user => items.All(item => item.IsVisibleStandalone(user));
    }

    /// <summary>
    /// Sends messages about an item to the devices of every user who may open it.
    /// </summary>
    /// <param name="item">What the messages are about.</param>
    /// <param name="notifications">The messages.</param>
    /// <returns>Expo's response, or null when nobody may be told.</returns>
    /// <remarks>
    /// A new movie or episode used to go to every registered device, so its title reached
    /// people who cannot open the library it is in, or are not allowed its rating. Jellyfin
    /// filtered its own new content notifications the same way before it dropped them, with
    /// <c>IsVisibleStandalone</c> for each user, which checks the library, the parental
    /// rating and the tags.
    /// </remarks>
    public Task<ExpoNotificationResponse?> SendToWhoCanOpen(BaseItem item, params ExpoNotificationRequest[] notifications)
    {
        ArgumentNullException.ThrowIfNull(item);

        return SendToWhoCanOpen([item], notifications);
    }

    /// <summary>
    /// Sends messages about several items to the devices of every user who may open all of
    /// them.
    /// </summary>
    /// <param name="items">
    /// Everything the messages describe. A user who may not open one of them is not told,
    /// since the message names it.
    /// </param>
    /// <param name="notifications">The messages.</param>
    /// <returns>Expo's response, or null when nobody may be told.</returns>
    /// <remarks>
    /// A new movie or episode used to go to every registered device, so its title reached
    /// people who cannot open the library it is in, or are not allowed its rating. Jellyfin
    /// filtered its own new content notifications the same way before it dropped them, with
    /// <c>IsVisibleStandalone</c> for each user, which checks the library, the parental
    /// rating and the tags.
    /// </remarks>
    public async Task<ExpoNotificationResponse?> SendToWhoCanOpen(IReadOnlyCollection<BaseItem> items, params ExpoNotificationRequest[] notifications)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(notifications);

        var subject = items.Count == 0 ? Guid.Empty : items.First().Id;

        if (_userManager is null)
        {
            _logger?.LogWarning("No user manager available, cannot work out who may be told about {Item}", subject);
            return null;
        }

        var devices = StreamyfinPlugin.Instance?.Database.GetAllDeviceTokens() ?? [];
        var canOpen = CanOpenEvery(items);

        // An empty id is no user, and GetUserById throws on it.
        var recipients = RecipientsWho(devices, userId =>
            !userId.Equals(default)
            && MayBeTold(_userManager.GetUserById(userId), canOpen));

        if (recipients.Count == 0)
        {
            _logger?.LogInformation(
                "No registered device belongs to a user who may open all {Count} item(s) of {Item}, so nothing was sent",
                items.Count,
                subject);
            return null;
        }

        _logger?.LogInformation(
            "Sending to {Recipients} of {Devices} registered device(s), those whose user may open all {Count} item(s) of {Item}",
            recipients.Count,
            devices.Select(device => device.Token).Distinct(StringComparer.Ordinal).Count(),
            items.Count,
            subject);

        foreach (var notification in notifications)
        {
            notification.To = recipients;
        }

        return await Send(notifications).ConfigureAwait(false);
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

    /// <summary>
    /// Sends messages to the devices they name, in as many requests as Expo needs.
    /// </summary>
    /// <param name="notifications">The messages, each already addressed.</param>
    /// <returns>
    /// The tickets from the batches Expo answered, in the order those recipients went
    /// out, or null when none was answered. A refused batch contributes no tickets, so
    /// the list lines up with the recipients batch by batch and not as a whole: nothing
    /// outside this method should match a ticket to a device by its index.
    /// </returns>
    /// <remarks>
    /// Expo takes a hundred recipients per request and refuses a body past that, whole.
    /// A server with more devices than that had its library notifications refused
    /// entirely, so nobody was told rather than everybody.
    ///
    /// <para>
    /// Each batch is reconciled against its own recipients rather than all of them at
    /// the end. A refused batch then costs only its own devices their pruning, instead
    /// of shifting every later ticket by one position and making the counts disagree,
    /// which <see cref="ExpoTickets.Reconcile"/> answers by doing nothing at all.
    /// </para>
    /// </remarks>
    public async Task<ExpoNotificationResponse?> Send(params ExpoNotificationRequest[] notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        var batches = ExpoBatching.Chunk(notifications);

        if (batches.Count > 1)
        {
            _logger?.LogInformation(
                "Sending to {Recipients} device(s) in {Batches} requests, since Expo takes {Limit} at a time",
                batches.Sum(batch => batch.Sum(notification => notification.To.Count)),
                batches.Count,
                ExpoBatching.MaxRecipientsPerRequest);
        }

        var tickets = new List<TicketStatus>();
        var errors = new List<Errors>();
        var answered = false;

        for (var sent = 0; sent < batches.Count; sent++)
        {
            var batch = batches[sent];

            // The order Expo answers in. One ticket comes back per recipient, and that
            // position is the only thing tying an error ticket to the device it came from.
            var recipients = batch.SelectMany(notification => notification.To).ToList();

            // When this batch left, so a device that registers the same token while Expo is
            // answering is not removed by what it says about the installation before it.
            var sentAt = DateTime.UtcNow;

            // No token to pass: a send happens inside a synchronous Jellyfin event handler,
            // which has none to give. The client timeout is what bounds it.
            var response = await PostToExpo<ExpoNotificationResponse>(
                SendUri,
                _serializationHelper.ToJson(batch),
                _retry,
                CancellationToken.None).ConfigureAwait(false);

            PruneAndQueue(recipients, response, sentAt);

            // Expo refusing one batch after its retries is Expo refusing this send. The
            // rest would be nine more batches of the same request, each with its own
            // waits, and PostNotifications blocks on this: a thousand recipients against
            // a server answering 429 would hold an event handler for twenty minutes to
            // reach nobody.
            if (response is null)
            {
                _logger?.LogWarning(
                    "Expo did not answer a batch, so the remaining {Batches} were not sent",
                    batches.Count - sent - 1);
                break;
            }

            answered = true;
            tickets.AddRange(response.Data);

            // What Expo says about the request rather than about a delivery. The caller
            // is handed this response and the notifications route serializes it, so a
            // batch's errors would otherwise be dropped on the way out.
            errors.AddRange(response.Errors);
        }

        return answered ? new ExpoNotificationResponse { Data = tickets, Errors = errors } : null;
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

        // Asking what became of a ticket changes nothing, so unlike a send it is worth
        // asking again when Expo answers 500 or never answers at all.
        return await PostToExpo<ExpoReceiptResponse>(
            ReceiptsUri,
            _serializationHelper.ToJson(new ExpoReceiptRequest { Ids = [.. ticketIds] }),
            _retryReads,
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
    private void PruneAndQueue(IReadOnlyList<string> recipients, ExpoNotificationResponse? response, DateTime sentAt)
    {
        var outcome = ExpoTickets.Reconcile(recipients, response, _logger);

        var database = StreamyfinPlugin.Instance?.Database;
        if (database is null)
        {
            return;
        }

        if (outcome.DeadTokens.Count > 0)
        {
            var removed = database.RemoveDeviceTokensNamed(
                outcome.DeadTokens.ToDictionary(token => token, _ => sentAt, StringComparer.Ordinal));

            _logger?.LogInformation(
                "Expo reported {Devices} device(s) as no longer registered, {Rows} token row(s) removed",
                outcome.DeadTokens.Count,
                removed);
        }

        if (outcome.Pending.Count > 0)
        {
            database.AddExpoReceipts(
                outcome.Pending.Select(pending => (pending.TicketId, pending.Token)),
                sentAt);
        }
    }

    private async Task<T?> PostToExpo<T>(
        string uri,
        string serializedRequest,
        ExpoRetry retry,
        CancellationToken cancellationToken)
        where T : class
    {
        _logger?.LogDebug("Preparing to call {Uri}", uri);

        for (var tried = 1; ; tried++)
        {
            // From the factory, never a new HttpClient per send: one built inline gets its own
            // connection pool every time and carries the default hundred second timeout, inside
            // an event handler that Jellyfin is waiting on.
            var client = _httpClientFactory.CreateClient(ExpoClientName);
            using var httpRequest = GetHttpRequestMessage(uri, serializedRequest);
            using var rawResponse = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

            // Expo answers 429 when it is being asked too often, and the body is then not a
            // ticket list. Read as one anyway it yields a response with no tickets, which every
            // caller reads as a delivery that simply had nothing to report.
            if (rawResponse.IsSuccessStatusCode)
            {
                _logger?.LogDebug("Received response");

                return await rawResponse.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
            }

            var body = await rawResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var shown = body.Length > 500 ? body[..500] : body;
            var wait = retry.Wait(rawResponse.StatusCode, rawResponse.Headers.RetryAfter, tried, DateTimeOffset.UtcNow);

            if (wait is null)
            {
                _logger?.LogError(
                    "Expo refused the request to {Uri} with {Status} after {Tries} tr(ies): {Body}",
                    uri,
                    (int)rawResponse.StatusCode,
                    tried,
                    shown);

                return null;
            }

            _logger?.LogWarning(
                "Expo answered {Status} for {Uri}, trying again in {Wait}: {Body}",
                (int)rawResponse.StatusCode,
                uri,
                wait.Value,
                shown);

            await Task.Delay(wait.Value, cancellationToken).ConfigureAwait(false);
        }
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