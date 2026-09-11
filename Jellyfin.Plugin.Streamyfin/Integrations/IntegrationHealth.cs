using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Streamyfin.Integrations;

/// <summary>
/// A third party service the plugin can point the app at.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IntegrationKind
{
    /// <summary>Seerr, the project formerly called Jellyseerr.</summary>
    Seerr,

    /// <summary>Marlin search.</summary>
    Marlin,

    /// <summary>Streamystats.</summary>
    Streamystats
}

/// <summary>
/// What a probe found.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IntegrationOutcome
{
    /// <summary>Nothing is configured, so there is nothing to reach.</summary>
    NotConfigured,

    /// <summary>The service answered and is the one it was meant to be.</summary>
    Ok,

    /// <summary>Something answered, but it is not the service that was expected.</summary>
    WrongService,

    /// <summary>Nothing answered.</summary>
    Unreachable,

    /// <summary>The URL is not one the server will try to open.</summary>
    NotAUrl
}

/// <summary>
/// One integration, as the admin form and the app both read it.
/// </summary>
/// <param name="Kind">Which service.</param>
/// <param name="Outcome">What the probe found.</param>
/// <param name="Detail">A sentence an administrator can act on.</param>
/// <param name="Version">The version the service reported, when it reports one.</param>
/// <remarks>
/// Deliberately carries no URL. The health of an integration is something every user
/// may know, since the app changes what it offers by it, but the address of an internal
/// service is not, and P1.4 exists because this plugin used to serve that distinction
/// the wrong way round.
/// </remarks>
public sealed record IntegrationHealth(
    [property: JsonPropertyName("kind")] IntegrationKind Kind,
    [property: JsonPropertyName("outcome")] IntegrationOutcome Outcome,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("version")] string? Version);
