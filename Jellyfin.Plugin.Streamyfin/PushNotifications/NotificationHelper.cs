using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.IO.Compression;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
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
    /// The devices of the users who may be told about something, one row per token.
    /// </summary>
    /// <param name="devices">The registered devices.</param>
    /// <param name="mayKnow">Whether a user may be told, by user id. Asked once per user.</param>
    /// <returns>The rows to send to, in the order they were given.</returns>
    internal static List<DeviceToken> DevicesWho(IEnumerable<DeviceToken> devices, Func<Guid, bool> mayKnow)
    {
        ArgumentNullException.ThrowIfNull(devices);
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

        return devices
            .Where(device => Allowed(device.UserId))
            .GroupBy(device => device.Token, StringComparer.Ordinal)
            .Select(sameToken => sameToken.First())
            .ToList();
    }

    /// <summary>
    /// The devices grouped by what a message written for them would say.
    /// </summary>
    /// <param name="devices">The devices to send to.</param>
    /// <returns>One group per audience, each with the tokens to write it for.</returns>
    /// <remarks>
    /// One message is written per audience rather than per device: a server with fifty
    /// phones in two languages and one address writes two messages, not fifty. A device
    /// that named no language is written to in the server's, and one that named no address
    /// gets a notification without its poster.
    /// </remarks>
    internal static List<(Audience Audience, List<string> Tokens)> ByAudience(IEnumerable<DeviceToken> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        return devices
            .GroupBy(
                device => (Language: DeviceLanguage.Stored(device.Language), Server: DeviceServer.Stored(device.ServerUrl)),
                new AudienceComparer())
            .Select(together => (
                new Audience(DeviceLanguage.CultureOf(together.Key.Language), together.Key.Server),
                together.Select(device => device.Token).Distinct(StringComparer.Ordinal).ToList()))
            .ToList();
    }

    // Two devices are written for together when they asked for the same language and reach
    // the server at the same address, both compared as they are stored.
    private sealed class AudienceComparer : IEqualityComparer<(string? Language, string? Server)>
    {
        public bool Equals((string? Language, string? Server) left, (string? Language, string? Server) right) =>
            string.Equals(left.Language, right.Language, StringComparison.Ordinal)
            && string.Equals(left.Server, right.Server, StringComparison.Ordinal);

        public int GetHashCode((string? Language, string? Server) key) =>
            HashCode.Combine(
                key.Language is null ? 0 : StringComparer.Ordinal.GetHashCode(key.Language),
                key.Server is null ? 0 : StringComparer.Ordinal.GetHashCode(key.Server));
    }

    /// <summary>
    /// Whether an event is worth building at all.
    /// </summary>
    /// <param name="eventKey">The event, by the key the configuration and the page use.</param>
    /// <param name="server">What the server says about it, which may be nothing.</param>
    /// <returns>True when the server wants it, or when some level asked for it.</returns>
    /// <remarks>
    /// Asked at the top of an event, where it used to be enough to read the server's own
    /// switch. It is the cheap half of the same question <see cref="SendForEvent"/> asks
    /// per account, and it exists so that an event nobody wants costs a read of two small
    /// tables rather than a message and a place in its own dedupe.
    /// </remarks>
    public bool Wants(string eventKey, NotificationConfiguration? server) =>
        NotificationTargets
            .From(StreamyfinPlugin.Instance?.Database)
            .AnybodyWants(eventKey, server is { Enabled: true });

    /// <summary>
    /// Sends an event to everyone it reaches, once the groups and the users have had their
    /// say about it.
    /// </summary>
    /// <param name="eventKey">The event, by the key the configuration and the page use.</param>
    /// <param name="server">What the server says about it, which may be nothing.</param>
    /// <param name="byDefault">
    /// Who the event is for when no level says otherwise: the administrators for the ones
    /// about the server itself, anybody for a new item.
    /// </param>
    /// <param name="andAlso">
    /// A rule the event cannot be given away from, or <c>null</c>. A new item is only
    /// announced to people who may open it, whatever a level says.
    /// </param>
    /// <param name="write">Writes the messages for one audience, called once per audience.</param>
    /// <returns>Expo's response, or null when nobody is to be told.</returns>
    public async Task<ExpoNotificationResponse?> SendForEvent(
        string eventKey,
        NotificationConfiguration? server,
        Func<User, bool> byDefault,
        Func<User, bool>? andAlso,
        Func<Audience, ExpoNotificationRequest[]> write)
    {
        ArgumentNullException.ThrowIfNull(byDefault);
        ArgumentNullException.ThrowIfNull(write);

        if (_userManager is null)
        {
            _logger?.LogWarning("No user manager available, cannot work out who {Event} reaches", eventKey);
            return null;
        }

        var devices = StreamyfinPlugin.Instance?.Database.GetAllDeviceTokens() ?? [];
        var targets = NotificationTargets.From(StreamyfinPlugin.Instance?.Database);
        var serverEnabled = server is { Enabled: true };

        var recipients = DevicesWho(devices, userId =>
        {
            if (userId.Equals(default))
            {
                return false;
            }

            var user = _userManager.GetUserById(userId);

            return user is not null
                && !user.IsDisabled()
                && targets.Reaches(eventKey, userId, serverEnabled, byDefault(user))
                && (andAlso?.Invoke(user) ?? true);
        });

        if (recipients.Count == 0)
        {
            _logger?.LogInformation("Nobody is to be told about {Event}, so nothing was sent", eventKey);
            return null;
        }

        return await SendToDevices(recipients, write).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends to these devices, each in the language it asked for.
    /// </summary>
    /// <param name="devices">Who to send to.</param>
    /// <param name="write">
    /// Writes the messages for one audience. Called once per audience among the devices, so
    /// it has to build its messages each time rather than hand back the same objects.
    /// </param>
    /// <returns>Expo's response, or null when there is nobody to send to.</returns>
    public async Task<ExpoNotificationResponse?> SendToDevices(
        IEnumerable<DeviceToken> devices,
        Func<Audience, ExpoNotificationRequest[]> write)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(write);

        var groups = ByAudience(devices);

        if (groups.Count == 0)
        {
            _logger?.LogInformation("No device to send to");
            return null;
        }

        var messages = new List<ExpoNotificationRequest>();

        foreach (var (audience, tokens) in groups)
        {
            foreach (var message in write(audience))
            {
                message.To = tokens;
                messages.Add(message);
            }
        }

        _logger?.LogInformation(
            "Sending {Messages} notification(s) to {Devices} device(s), written {Audiences} time(s)",
            messages.Count,
            groups.Sum(group => group.Tokens.Count),
            groups.Count);

        return await Send([.. messages]).ConfigureAwait(false);
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

                return await ReadAnswer<T>(rawResponse.Content, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Reads what Expo answered, whatever it is wrapped in.
    /// </summary>
    /// <remarks>
    /// The request used to say <c>Accept-Encoding: gzip, deflate</c> and nothing
    /// unwrapped what came back, so every answer arrived compressed and was parsed as
    /// JSON: <c>'0x1F' is an invalid start of value</c>, 0x1F being the first byte of a
    /// gzip stream. The hourly receipts task failed with it on a real server, and every
    /// send lost its answer, which is what prunes a dead token and queues its receipt.
    ///
    /// <para>
    /// The request no longer asks for it and the registered client decompresses by
    /// itself. This stays because a proxy in front of a server compresses whatever it
    /// likes, and reading the answer is worth more than being right about who asked.
    /// </para>
    /// </remarks>
    // The same options ReadFromJsonAsync uses, which is what the plain path reads with:
    // web defaults, so "status" matches Status. Deserialising with the bare defaults
    // instead parses the document and hands back a response with nothing in it.
    private static readonly JsonSerializerOptions _asExpoWritesIt = new(JsonSerializerDefaults.Web);

    private static async Task<T?> ReadAnswer<T>(HttpContent content, CancellationToken cancellationToken)
        where T : class
    {
        var encoding = content.Headers.ContentEncoding.LastOrDefault();

        if (encoding is null)
        {
            return await content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
        }

        var raw = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var plain = encoding.ToUpperInvariant() switch
        {
            "GZIP" => new GZipStream(raw, CompressionMode.Decompress),
            "DEFLATE" => new DeflateStream(raw, CompressionMode.Decompress),
            "BR" => (Stream)new BrotliStream(raw, CompressionMode.Decompress),
            _ => raw
        };

        try
        {
            return await JsonSerializer.DeserializeAsync<T>(plain, _asExpoWritesIt, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!ReferenceEquals(plain, raw))
            {
                await plain.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static HttpRequestMessage GetHttpRequestMessage(string uri, string content) => new()
    {
        Method = HttpMethod.Post,
        RequestUri = new Uri(uri),
        Headers =
        {
            { "Host", "exp.host" },
            { "Accept", "application/json" }
        },
        Content = new StringContent(
            content: content,
            encoding: Encoding.UTF8,
            mediaType: "application/json"
        )
    };
}