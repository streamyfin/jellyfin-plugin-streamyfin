using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
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

        if (!Address(url, out var address))
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
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            _logger?.LogDebug(exception, "Probing {Kind} did not reach it", kind);

            return new IntegrationHealth(
                kind,
                IntegrationOutcome.Unreachable,
                "Nothing answered at that address from this server.",
                null);
        }
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

    /// <summary>
    /// Whether an address is one the server will try to open.
    /// </summary>
    /// <param name="url">The address, as it was typed.</param>
    /// <param name="address">The address, when it is one.</param>
    /// <returns>Whether it is.</returns>
    /// <remarks>
    /// Absolute, and http or https. A <c>file:</c> address would have the server read
    /// its own disk and report whether it succeeded, which is a probe answering a
    /// question nobody asked.
    /// </remarks>
    public static bool Address(string? url, out Uri? address)
    {
        address = null;

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        address = parsed;
        return true;
    }

    // /api/v1/status is Seerr's own, unauthenticated, and carries a version. It proves
    // both that something answered and that it is the right something, which is the one
    // integration here where that can be told apart.
    private async Task<IntegrationHealth> Seerr(Uri address, CancellationToken cancellationToken)
    {
        var status = new Uri(address.ToString().TrimEnd('/') + "/api/v1/status");

        using var response = await _clients.CreateClient(ClientName)
            .GetAsync(status, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new IntegrationHealth(
                IntegrationKind.Seerr,
                IntegrationOutcome.WrongService,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Something answered with {0}, which Seerr's status endpoint would not.",
                    (int)response.StatusCode),
                null);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var version = SeerrVersion(body);

        return version is null
            ? new IntegrationHealth(
                IntegrationKind.Seerr,
                IntegrationOutcome.WrongService,
                "Something answered, but it did not answer like Seerr.",
                null)
            : new IntegrationHealth(IntegrationKind.Seerr, IntegrationOutcome.Ok, null, version);
    }

    // No known endpoint identifies these, so any HTTP answer, a 404 included, is the
    // most that can honestly be claimed: the host is up and speaking HTTP on this
    // scheme and port. It cannot tell a wrong service from a right one.
    private async Task<IntegrationHealth> Answers(IntegrationKind kind, Uri address, CancellationToken cancellationToken)
    {
        using var response = await _clients.CreateClient(ClientName)
            .GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

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
                && version.ValueKind == JsonValueKind.String)
            {
                return version.GetString();
            }

            return document.RootElement.TryGetProperty("commitTag", out _) ? "unknown" : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
