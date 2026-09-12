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

    /// <summary>
    /// Something is serving HTTP there, and nothing says what. Apart from
    /// <see cref="Ok"/> so a consumer can tell "confirmed" from "answers".
    /// </summary>
    Reachable,

    /// <summary>Something answered, but it is not the service that was expected.</summary>
    WrongService,

    /// <summary>
    /// Something in front of the service says the service is not working.
    /// </summary>
    Down,

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
/// Carries no URL on purpose: that a service is down is every user's to know, where it
/// lives is not.
/// </remarks>
public sealed record IntegrationHealth(
    [property: JsonPropertyName("kind")] IntegrationKind Kind,
    [property: JsonPropertyName("outcome")] IntegrationOutcome Outcome,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("version")] string? Version);
