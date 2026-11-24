namespace Jellyfin.Plugin.Streamyfin.Db;

/// <summary>
/// The configuration the server declares for everyone, which is the first of the
/// three targeting levels.
/// </summary>
/// <remarks>
/// It lived in Jellyfin's own plugin configuration XML until P1.5, which left the
/// three levels in two different stores: the global one in a file the server owned,
/// the other two in this database. Anything that had to reason about all three had to
/// know about both.
///
/// <para>
/// Two rows. <see cref="Current"/> is the configuration. <see cref="LegacyFile"/> is
/// the old file as this version last read it, which is how a start after a downgrade
/// and an update tells what the older build changed there.
/// </para>
///
/// <para>
/// Stored as JSON for the same reason a group's overrides are: the configuration
/// gains a setting far more often than it gains a shape, and forty five columns
/// would turn every new setting into a migration.
/// </para>
/// </remarks>
public class GlobalConfiguration
{
    /// <summary>
    /// The configuration's key. A fixed value rather than an identity column, so a bug
    /// that writes twice overwrites rather than quietly giving the server a second
    /// configuration it will never read.
    /// </summary>
    public const string Current = "current";

    /// <summary>
    /// The key of the copy of Jellyfin's plugin XML, as this version last read it.
    /// </summary>
    public const string LegacyFile = "legacy-file";

    /// <summary>
    /// Gets or sets the row key.
    /// </summary>
    public string Id { get; set; } = Current;

    /// <summary>
    /// Gets or sets the configuration, as JSON.
    /// </summary>
    public string ConfigJson { get; set; } = "{}";
}
