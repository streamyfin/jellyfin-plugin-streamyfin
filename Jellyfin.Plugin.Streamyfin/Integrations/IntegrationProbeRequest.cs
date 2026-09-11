using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Streamyfin.Integrations;

/// <summary>
/// Asking the server to try an address before it is saved.
/// </summary>
/// <remarks>
/// The address is sent rather than read from the configuration on purpose: the point of
/// the button is to answer before the form is saved, so that a wrong address is caught
/// while it is still on screen rather than by a user reporting an empty tab.
/// </remarks>
public class IntegrationProbeRequest
{
    /// <summary>
    /// Gets or sets which service to try.
    /// </summary>
    /// <remarks>
    /// Nullable so that leaving it out is a refusal rather than a silent answer about
    /// Seerr: <c>Required</c> on a plain enum is satisfied by any value, and the first
    /// member is what a missing field parses as.
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
