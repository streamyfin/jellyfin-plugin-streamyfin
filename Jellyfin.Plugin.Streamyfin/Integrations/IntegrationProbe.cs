using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
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
/// This belongs on the server rather than in the app, and that is the whole point of it.
/// An administrator types an address the server can reach and a phone on mobile data
/// never will, saves it, and finds out it is wrong when a user reports that the Seerr
/// tab is empty. The server can ask, from where the address is meant to work.
///
/// <para>
/// The probes are the ones the app already uses, deliberately: Seerr has an
/// unauthenticated endpoint that identifies the service, and the other two do not, so
/// for those any HTTP answer at all is the most that can honestly be claimed. A probe
/// resolves rather than throws, because a failed probe is an answer.
/// </para>
/// </remarks>
public sealed class IntegrationProbe
{
    /// <summary>
    /// The name of the configured client that reaches an integration. Its timeout lives
    /// with the registration rather than at this call site.
    /// </summary>
    public const string ClientName = "streamyfin-integrations";

    /// <summary>
    /// How long an answer is reused. Short enough that an administrator correcting an
    /// address sees it, long enough that a room full of apps starting at once costs one
    /// round of probes.
    /// </summary>
    private static readonly TimeSpan _keepFor = TimeSpan.FromSeconds(30);

    private readonly List<IntegrationHealth> _recent = [];
    private readonly IHttpClientFactory _clients;
    private readonly ILogger<IntegrationProbe>? _logger;

    private string? _recentFor;
    private DateTimeOffset _recentAt;

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
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            _logger?.LogDebug(exception, "Probing {Kind} did not reach it", kind);

            return new IntegrationHealth(
                kind,
                IntegrationOutcome.Unreachable,
                "Nothing answered at that address from this server.",
                null);
        }
        catch (UriFormatException exception)
        {
            // Distinct from nothing answering, because nothing was asked. Saying a
            // connection was attempted when none was would send an administrator
            // looking at their network.
            _logger?.LogDebug(exception, "Could not build a request for {Kind}", kind);

            return new IntegrationHealth(
                kind,
                IntegrationOutcome.NotAUrl,
                "The server could not turn that into a request.",
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
    /// Every signed in account may ask, each ask reaches three third party services from
    /// the server's own network position, and one that does not answer holds the request
    /// for the client timeout. A handful of apps starting at once, or one account in a
    /// loop, would turn this into something pointed at the administrator's own services.
    ///
    /// <para>
    /// Keyed by the addresses, so an administrator who corrects one gets a fresh answer
    /// on the next ask rather than the old one for another half minute, and shared
    /// across callers because the addresses are the server's rather than theirs.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<IntegrationHealth>> HealthOf(
        Settings? settings,
        CancellationToken cancellationToken = default)
    {
        var asked = string.Join(
            '\n',
            settings?.jellyseerrServerUrl?.value,
            settings?.marlinServerUrl?.value,
            settings?.streamyStatsServerUrl?.value);

        lock (_recent)
        {
            if (_recentFor == asked && DateTimeOffset.UtcNow - _recentAt < _keepFor && _recent.Count > 0)
            {
                return [.. _recent];
            }
        }

        var health = await ProbeAll(settings, cancellationToken).ConfigureAwait(false);

        lock (_recent)
        {
            _recent.Clear();
            _recent.AddRange(health);
            _recentFor = asked;
            _recentAt = DateTimeOffset.UtcNow;
        }

        return health;
    }

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
        var seerr = Probe(IntegrationKind.Seerr, settings?.jellyseerrServerUrl?.value, cancellationToken);
        var marlin = Probe(IntegrationKind.Marlin, settings?.marlinServerUrl?.value, cancellationToken);
        var stats = Probe(IntegrationKind.Streamystats, settings?.streamyStatsServerUrl?.value, cancellationToken);

        return [
            await seerr.ConfigureAwait(false),
            await marlin.ConfigureAwait(false),
            await stats.ConfigureAwait(false)
        ];
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
            return Refused(IntegrationKind.Seerr, response.StatusCode, "which Seerr's status endpoint would not");
        }

        var body = await FirstOf(response, cancellationToken).ConfigureAwait(false);
        var version = SeerrVersion(body);

        return version is null
            ? new IntegrationHealth(
                IntegrationKind.Seerr,
                IntegrationOutcome.WrongService,
                "Something answered, but it did not answer like Seerr.",
                null)
            : new IntegrationHealth(
                IntegrationKind.Seerr,
                IntegrationOutcome.Ok,
                null,
                string.IsNullOrWhiteSpace(version) ? null : version);
    }

    // A status document is a few hundred bytes. Reading whatever answers without a cap
    // would let a mistyped address pointing at a media file or a log tail pull as much
    // as eight seconds of it into memory, twice over as a string.
    private static async Task<string> FirstOf(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int Enough = 8 * 1024;

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[Enough];
        var filled = 0;

        while (filled < Enough)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(filled, Enough - filled), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, filled);
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

        // A 5xx is the one answer that is worse than no answer: something is there, in
        // front of the service, saying the service is not working. Reported as reachable
        // it would paint a stopped container green and send the app at a dead tab, which
        // is the failure this exists to catch.
        if ((int)response.StatusCode >= 500)
        {
            return Refused(kind, response.StatusCode, "which is something in front of the service saying it is not working");
        }

        return new IntegrationHealth(
            kind,
            IntegrationOutcome.Ok,
            string.Format(
                CultureInfo.InvariantCulture,
                "Answered with {0}. Nothing there identifies the service, so this only says the address is reachable.",
                (int)response.StatusCode),
            null);
    }

    // A JSON body carrying version or commitTag is a real Seerr, which is what the app's
    // own probe looks for. Anything else, including a login page that answers 200, is not.
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
                return version.GetString();
            }

            // A build that reports only a commit tag is still Seerr, and the tag is still
            // a version. A key that is there but null or a number is not: the answer said
            // nothing, and reporting an invented version would be worse than reporting
            // none.
            return document.RootElement.TryGetProperty("commitTag", out var tag)
                && tag.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(tag.GetString())
                    ? tag.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
