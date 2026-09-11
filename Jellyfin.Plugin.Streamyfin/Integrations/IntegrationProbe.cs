using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.Integrations;

/// <summary>
/// Asks a configured integration whether it is there.
/// </summary>
/// <remarks>
/// On the server because an administrator types an address the server reaches and a
/// phone on mobile data never will. Seerr has an endpoint that identifies it; the
/// others do not, so for those any HTTP answer is the most that can be claimed. A probe
/// answers rather than throws, since a failed probe is an answer.
/// </remarks>
public sealed class IntegrationProbe : IDisposable
{
    /// <summary>
    /// The configured client that reaches an integration.
    /// </summary>
    public const string ClientName = "streamyfin-integrations";

    private readonly TimeSpan _keepFor;

    // A cap on connections this opens at once, on both routes. The cache stops one
    // account in a loop; this stops the fan-out an address overridden per user makes
    // reachable, and the probe route, which takes an address rather than a cached set.
    private readonly SemaphoreSlim _atOnce = new(6, 6);

    // One answer, not one per address set. A server has one set; the ones that give a
    // group its own address have a handful, and re-probing when the set changes is
    // cheaper than a table with an expiry policy of its own.
    private readonly object _keeping = new();

    private IReadOnlyList<IntegrationHealth> _recent = [];
    private string? _recentFor;
    private DateTimeOffset _recentAt;
    private readonly IHttpClientFactory _clients;
    private readonly ILogger<IntegrationProbe>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntegrationProbe"/> class.
    /// </summary>
    /// <param name="clients">The factory holding the configured client.</param>
    /// <param name="loggerFactory">Where a probe is reported.</param>
    /// <param name="keepFor">How long an answer is reused. Half a minute unless a test says otherwise.</param>
    public IntegrationProbe(IHttpClientFactory clients, ILoggerFactory? loggerFactory = null, TimeSpan? keepFor = null)
    {
        _clients = clients;
        _logger = loggerFactory?.CreateLogger<IntegrationProbe>();
        _keepFor = keepFor ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Asks one service whether it is there.
    /// </summary>
    /// <param name="kind">Which service.</param>
    /// <param name="url">The address, as an administrator typed it.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>What was found. Never throws.</returns>
    public async Task<IntegrationHealth> Probe(
        IntegrationKind kind,
        string? url,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new IntegrationHealth(kind, IntegrationOutcome.NotConfigured, "Nothing is configured.", null);
        }

        if (!WebAddress.Parses(url, out var address))
        {
            return new IntegrationHealth(
                kind,
                IntegrationOutcome.NotAUrl,
                "That is not an http or https address the server will open.",
                null);
        }

        await _atOnce.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return kind == IntegrationKind.Seerr
                ? await Seerr(address!, cancellationToken).ConfigureAwait(false)
                : await Answers(kind, address!, cancellationToken).ConfigureAwait(false);
        }
        // The caller going away is not a verdict about the address. A timeout is.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UriFormatException exception)
        {
            // Nothing was asked, so saying nothing answered would send an administrator
            // looking at their network.
            _logger?.LogDebug(exception, "Could not build a request for {Kind}", kind);

            return new IntegrationHealth(
                kind,
                IntegrationOutcome.NotAUrl,
                "The server could not turn that into a request.",
                null);
        }
        // Everything else, because a round is stored and replayed: one that faults is a
        // 500 for every caller for the next half minute.
        catch (Exception exception)
        {
            var said = Why(exception);

            // Anything Why could not name is a fault here rather than a network, and it
            // is reported to the administrator as a network. A debug line would leave
            // that unfalsifiable from the server.
            if (said == Unnamed)
            {
                _logger?.LogWarning(exception, "Probing {Kind} failed in a way nothing here expected", kind);
            }
            else
            {
                _logger?.LogDebug(exception, "Probing {Kind} did not reach it", kind);
            }

            return new IntegrationHealth(kind, IntegrationOutcome.Unreachable, said, null);
        }
        finally
        {
            _atOnce.Release();
        }
    }

    /// <summary>
    /// The health of every service these settings configure, reusing a recent answer.
    /// </summary>
    /// <param name="settings">The settings, resolved for whoever is asking.</param>
    /// <param name="cancellationToken">Stops the calls.</param>
    /// <returns>One answer per service, configured or not.</returns>
    /// <remarks>
    /// Every signed in account may ask, and each ask reaches the configured services
    /// from the server's own network position. Keyed by the addresses, so correcting one
    /// is answered at once rather than after the cache expires.
    /// </remarks>
    public async Task<IReadOnlyList<IntegrationHealth>> HealthOf(
        Settings? settings,
        CancellationToken cancellationToken = default)
    {
        var probeable = Probeable(settings);

        // Lengths rather than a separator: a stored address is not checked again on
        // read, and one carrying the separator could otherwise look like another set.
        var asked = string.Concat(probeable.Select(one => $"{(int)one.Kind}:{one.Url?.Length ?? -1}:{one.Url}"));

        lock (_keeping)
        {
            if (_recentFor == asked && DateTimeOffset.UtcNow - _recentAt < _keepFor)
            {
                return _recent;
            }
        }

        // On its own token: the answer is kept for everyone, and a caller who walks away
        // must not write theirs into it.
        var health = await ProbeEach(probeable, CancellationToken.None).ConfigureAwait(false);

        lock (_keeping)
        {
            _recent = health;
            _recentFor = asked;
            _recentAt = DateTimeOffset.UtcNow;
        }

        return health;
    }

    // Read from the declarations rather than from a list here, so a fourth integration
    // is an attribute on its property and nothing else.
    private static List<(IntegrationKind Kind, string? Url)> Probeable(Settings? settings)
    {
        var found = new List<(IntegrationKind, string?)>();

        foreach (var descriptor in SettingsSchema.Descriptors)
        {
            if (descriptor.Probe is null)
            {
                continue;
            }

            found.Add((descriptor.Probe.Kind, descriptor.Read(settings) as string));
        }

        return found;
    }

    /// <inheritdoc/>
    public void Dispose() => _atOnce.Dispose();

    /// <summary>
    /// The same answers with the versions removed.
    /// </summary>
    /// <param name="health">What the probes found.</param>
    /// <returns>The answers a caller who is not an administrator may read.</returns>
    /// <remarks>
    /// A development build reports its full commit tag, and the exact build of a private
    /// service is the usual first step in picking a published vulnerability for it. That
    /// an integration is down is every user's to know; which build it is is not.
    /// </remarks>
    public static IReadOnlyList<IntegrationHealth> WithoutVersions(IReadOnlyList<IntegrationHealth> health)
    {
        ArgumentNullException.ThrowIfNull(health);

        return [.. health.Select(one => one.Version is null ? one : one with { Version = null })];
    }

    /// <summary>
    /// Asks every service these settings configure, reaching all of them every time.
    /// </summary>
    /// <param name="settings">The settings, resolved for whoever is asking.</param>
    /// <param name="cancellationToken">Stops the calls.</param>
    /// <returns>One answer per service, configured or not.</returns>
    /// <remarks>
    /// Internal on purpose. <see cref="HealthOf"/> is what a route calls: this one
    /// reaches the services on every call, which is what the cache exists to stop.
    /// </remarks>
    internal async Task<IReadOnlyList<IntegrationHealth>> ProbeAll(
        Settings? settings,
        CancellationToken cancellationToken = default)
    {
        return await ProbeEach(Probeable(settings), cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<IntegrationHealth>> ProbeEach(
        List<(IntegrationKind Kind, string? Url)> probeable,
        CancellationToken cancellationToken)
    {
        var asking = probeable.Select(one => Probe(one.Kind, one.Url, cancellationToken)).ToList();

        return await Task.WhenAll(asking).ConfigureAwait(false);
    }

    // /api/v1/status is Seerr's own, unauthenticated, and carries a version. It proves
    // both that something answered and that it is the right something, which is the one
    // integration here where that can be told apart.
    private async Task<IntegrationHealth> Seerr(Uri address, CancellationToken cancellationToken)
    {
        var status = new UriBuilder(address)
        {
            // The path, not the whole address: a query or a fragment an administrator
            // pasted would otherwise be concatenated into the middle of the path, and
            // Seerr would answer its login page to what is no longer a status request.
            Path = address.AbsolutePath.TrimEnd('/') + "/api/v1/status",
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;

        using var response = await _clients.CreateClient(ClientName)
            .GetAsync(status, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // A 503 from a Seerr that is restarting is not a wrong address.
            return Ailing(IntegrationKind.Seerr, response.StatusCode)
                ?? Refused(IntegrationKind.Seerr, response.StatusCode, "which Seerr's status endpoint would not");
        }

        var (body, whole) = await FirstOf(response, cancellationToken).ConfigureAwait(false);
        var version = SeerrVersion(body);

        if (version is not null)
        {
            return new IntegrationHealth(IntegrationKind.Seerr, IntegrationOutcome.Ok, null, version);
        }

        // One that filled the cap is not a status document, and calling that the wrong
        // address diagnoses the wrong thing.
        return whole
            ? new IntegrationHealth(
                IntegrationKind.Seerr,
                IntegrationOutcome.WrongService,
                "Something answered, but it did not answer like Seerr.",
                null)
            : new IntegrationHealth(
                IntegrationKind.Seerr,
                IntegrationOutcome.Reachable,
                "Something answered with more than a status document, so this only says the address is reachable.",
                null);
    }

    // The sentence an administrator can act on. A refused certificate sends them to the
    // certificate rather than to their firewall, and a timeout is not a wrong name.
    private static string Why(Exception exception)
    {
        for (var cause = exception; cause is not null; cause = cause.InnerException)
        {
            if (cause is AuthenticationException)
            {
                return "Something answered, but the server would not accept its certificate.";
            }

            if (cause is SocketException socket)
            {
                switch (socket.SocketErrorCode)
                {
                    case SocketError.HostNotFound:
                    case SocketError.NoData:
                    case SocketError.TryAgain:
                        return "That host could not be resolved from this server.";

                    case SocketError.TimedOut:
                        return "Nothing answered in time from this server.";

                    case SocketError.ConnectionRefused:
                        return "That host answered, and nothing is listening on that port.";

                    case SocketError.NetworkUnreachable:
                    case SocketError.HostUnreachable:
                        return "That host cannot be reached from this server.";

                    default:
                        return Unnamed;
                }
            }

            if (cause is TimeoutException or TaskCanceledException)
            {
                return "Nothing answered in time from this server.";
            }
        }

        return Unnamed;
    }

    private const string Unnamed = "Nothing answered at that address from this server.";

    // A server error, an access wall and a redirect are each something other than a
    // wrong address.
    private static IntegrationHealth? Ailing(IntegrationKind kind, HttpStatusCode status)
    {
        if ((int)status >= 500)
        {
            return new IntegrationHealth(
                kind,
                IntegrationOutcome.Down,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Something answered with {0}, which is the service saying it is not working rather than the address being wrong.",
                    (int)status),
                null);
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new IntegrationHealth(
                kind,
                IntegrationOutcome.Reachable,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Something answered with {0}, so an access layer is in the way and the service itself could not be asked.",
                    (int)status),
                null);
        }

        if ((int)status >= 300 && (int)status < 400)
        {
            // Seerr is asked at its status endpoint, where a redirect means the request
            // never reached it. The others are asked at their root, where a redirect to
            // a login path or to https is how a service normally answers.
            return new IntegrationHealth(
                kind,
                IntegrationOutcome.Reachable,
                string.Format(
                    CultureInfo.InvariantCulture,
                    kind == IntegrationKind.Seerr
                        ? "That address answered with {0} and sends the request somewhere else, which was not followed. Use the address it redirects to."
                        : "Answered with {0}, a redirect, which was not followed. Nothing there identifies the service.",
                    (int)status),
                null);
        }

        return null;
    }

    // Capped: a mistyped address pointing at a media file would otherwise pull as much
    // as the timeout allows into memory. Whether it ended tells "not Seerr" from
    // "more than this could read".
    private static async Task<(string Body, bool Whole)> FirstOf(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        const int Enough = 8 * 1024;

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        // One byte past the cap, so a body that ends exactly on it is not mistaken for
        // one that was cut short.
        var buffer = new byte[Enough + 1];
        var filled = 0;
        var whole = false;

        while (filled < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(filled, buffer.Length - filled), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                whole = true;
                break;
            }

            filled += read;
        }

        return (Encoding.UTF8.GetString(buffer, 0, Math.Min(filled, Enough)), whole);
    }

    private static IntegrationHealth Refused(IntegrationKind kind, HttpStatusCode status, string why) =>
        new(
            kind,
            IntegrationOutcome.WrongService,
            string.Format(CultureInfo.InvariantCulture, "Something answered with {0}, {1}.", (int)status, why),
            null);

    // No known endpoint identifies these, so what answers cannot be told apart from
    // what should have. A status below 500 is still something serving HTTP at this
    // address, which is all that can honestly be claimed.
    private async Task<IntegrationHealth> Answers(IntegrationKind kind, Uri address, CancellationToken cancellationToken)
    {
        using var response = await _clients.CreateClient(ClientName)
            .GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        var ailing = Ailing(kind, response.StatusCode);
        if (ailing is not null)
        {
            return ailing;
        }

        // Reachable rather than Ok: the Jellyfin address in the Marlin field answers
        // 200 and lands here, and an app reading that as confirmed opens a dead tab.
        return new IntegrationHealth(
            kind,
            IntegrationOutcome.Reachable,
            string.Format(
                CultureInfo.InvariantCulture,
                "Answered with {0}. Nothing there identifies the service, so this only says the address is reachable.",
                (int)response.StatusCode),
            null);
    }

    // version or commitTag is a real Seerr. A login page answering 200 is not.
    // Echoed to every signed in user by the health route, and it comes from whatever is
    // at the address, so it is bounded here where the contract is.
    private const int LongestVersion = 64;

    private static string? Short(string? version)
    {
        if (version is null || version.Length <= LongestVersion)
        {
            return version;
        }

        // Not through a surrogate pair, which would leave half a character for the app
        // and the page to render as a replacement glyph.
        var cut = LongestVersion;
        if (char.IsHighSurrogate(version[cut - 1]))
        {
            cut--;
        }

        return version[..cut];
    }

    private static string? SeerrVersion(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (document.RootElement.TryGetProperty("version", out var version)
                && version.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(version.GetString()))
            {
                return Short(version.GetString());
            }

            // A commit tag is a version. One that is null or a number said nothing, and
            // an invented version is worse than none.
            return document.RootElement.TryGetProperty("commitTag", out var tag)
                && tag.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(tag.GetString())
                    ? Short(tag.GetString())
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
