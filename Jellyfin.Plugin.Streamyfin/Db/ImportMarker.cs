using System;

namespace Jellyfin.Plugin.Streamyfin.Db;

/// <summary>
/// Records that a one time import has run, so it never runs twice.
/// </summary>
/// <remarks>
/// The marker is written inside the same transaction as the rows it covers. An
/// import that fails leaves no marker and no rows, so the next start retries it.
/// </remarks>
public class ImportMarker
{
    /// <summary>
    /// The import of device tokens from the hand written SQLite store.
    /// </summary>
    public const string LegacyDeviceTokens = "legacy-device-tokens";

    /// <summary>
    /// Gets or sets the name of the import.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when it ran.
    /// </summary>
    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>
    /// Gets or sets how many rows it carried over.
    /// </summary>
    public int RowsImported { get; set; }
}
