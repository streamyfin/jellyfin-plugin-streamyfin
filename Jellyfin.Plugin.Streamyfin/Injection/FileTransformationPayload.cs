using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Streamyfin.Injection;

/// <summary>
/// What the File Transformation plugin hands a callback.
/// </summary>
/// <remarks>
/// Declared here rather than referenced, because that plugin cannot be referenced as
/// a library: Jellyfin loads each plugin into its own load context, so the same type
/// coming from two assemblies is two different types. The shape is documented in its
/// README and is one property.
/// </remarks>
public class FileTransformationPayload
{
    /// <summary>
    /// Gets or sets the file as it stands before this callback runs.
    /// </summary>
    [JsonPropertyName("contents")]
    public string? Contents { get; set; }
}
