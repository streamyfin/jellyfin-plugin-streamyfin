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
public sealed class IntegrationProbe
{
    /// <summary>
    /// The configured client that reaches an integration.
    /// </summary>
    public const string ClientName = "streamyfin-integrations";

    /// <summary>
    /// How long an answer is reused.
    /// </summary>
    private static readonly TimeSpan _keepFor = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, Round> _recent = new(StringComparer.Ordinal);
    private readonly IHttpClientFactory _clients;
    private readonly ILogger<IntegrationProbe>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntegrationProbe"/> class.
    /// </summary>
    /// <param name="clients">The factory holding the configured client.</param>
    /// <param name="loggerFactory">Where a probe is reported.</param>
    public IntegrationProbe(IHttpClientFactory clients, ILoggerFactory? loggerFactory = null)
    {
        _clients = clients;
        _logger = loggerFactory?.CreateLogger<IntegrationProbe>();
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
            _logger?.LogDebug(exception, "Probing {Kind} did not reach it", kind);

            return new IntegrationHealth(
                kind,
                IntegrationOutcome.Unreachable,
                Why(exception),
                null);
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
        var asked = string.Join('\n', probeable.Select(one => $"{one.Kind}={one.Url}"));
        Lazy<Task<IReadOnlyList<IntegrationHealth>>> round;

        lock (_recent)
        {
            Forget(DateTimeOffset.UtcNow);

            if (!_recent.TryGetValue(asked, out var known))
            {
                // The round runs on its own token, since this answer is stored for
                // everyone and a caller who walks away must not write theirs into it.
                // Lazy, so the lock publishes the task rather than doing its work, and
                // over the addresses rather than the whole settings graph.
                known = new Round(
                    new Lazy<Task<IReadOnlyList<IntegrationHealth>>>(() => ProbeEach(probeable, CancellationToken.None)),
                    DateTimeOffset.UtcNow);
                _recent[asked] = known;
            }

            round = known.Health;
        }

        return await round.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Anything past its half minute, so a server giving many users their own address
    // does not keep a slot per address for ever.
    private void Forget(DateTimeOffset now)
    {
        if (_recent.Count == 0)
        {
            return;
        }

        List<string>? stale = null;

        foreach (var entry in _recent)
        {
            if (now - entry.Value.At >= _keepFor)
            {
                (stale ??= []).Add(entry.Key);
            }
        }

        foreach (var key in stale ?? [])
        {
            _recent.Remove(key);
        }
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

            var lockable = settings is null ? null : descriptor.Property.GetValue(settings);
            var url = lockable is null ? null : descriptor.Value?.GetValue(lockable) as string;
            found.Add((descriptor.Probe.Kind, url));
        }

        return found;
    }

    private sealed record Round(Lazy<Task<IReadOnlyList<IntegrationHealth>>> Health, DateTimeOffset At);

    /// <summary>
    /// Asks every service these settings configure.
    /// </summary>
    /// <param name="settings">The settings, resolved for whoever is asking.</param>
    /// <param name="cancellationToken">Stops the calls.</param>
    /// <returns>One answer per service, configured or not.</returns>
    public async Task<IReadOnlyList<IntegrationHealth>> ProbeAll(
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

            if (cause is SocketException socket && socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
            {
                return "That host could not be resolved from this server.";
            }
        }

        return exception is TaskCanceledException
            ? "Nothing answered in time from this server."
            : "Nothing answered at that address from this server.";
    }

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
            return new IntegrationHealth(
                kind,
                IntegrationOutcome.Reachable,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "That address answered with {0} and sends the request somewhere else, which was not followed. Use the address it redirects to.",
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

    private static string? Short(string? version) =>
        version is null || version.Length <= LongestVersion ? version : version[..LongestVersion];

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
