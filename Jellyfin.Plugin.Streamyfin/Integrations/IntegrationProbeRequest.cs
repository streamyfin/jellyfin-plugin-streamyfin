using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Streamyfin.Integrations;

/// <summary>
/// Asking the server to try an address before it is saved.
/// </summary>
/// <remarks>
/// The address is sent rather than read from the configuration, so a wrong one is caught
/// while it is still on screen.
/// </remarks>
public class IntegrationProbeRequest
{
    /// <summary>
    /// Gets or sets which service to try.
    /// </summary>
    /// <remarks>
    /// Nullable, or a missing field parses as the first member and answers about Seerr.
    /// </remarks>
    [Required]
    [JsonPropertyName("kind")]
    public IntegrationKind? Kind { get; set; }

    /// <summary>
    /// Gets or sets the address to try, as it was typed.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
