using System;

namespace Jellyfin.Plugin.Streamyfin.Db;

/// <summary>
/// A named set of users an administrator can target settings at.
/// </summary>
/// <remarks>
/// Jellyfin has a <c>Group</c> entity of its own, with permissions and preferences,
/// but nothing uses it: no manager, no controller, no route. So the plugin defines
/// its own rather than building on something the server does not expose. If that
/// ever changes, this table is what maps onto it.
/// </remarks>
public class SettingsGroup
{
    /// <summary>
    /// Gets or sets the group id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the name an administrator sees.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets which group wins when a user is in more than one.
    /// </summary>
    /// <remarks>
    /// Higher wins, because it is layered later. Groups sharing a priority are
    /// ordered by id, which is arbitrary but stable, so the same user always
    /// resolves to the same answer instead of to whatever the database returned
    /// first.
    /// </remarks>
    public int Priority { get; set; }

    /// <summary>
    /// Gets or sets the settings this group says something about, as JSON.
    /// </summary>
    /// <remarks>
    /// A partial <c>Settings</c>: only the keys the group means to override are
    /// filled in, which works because every property on it is nullable. Stored as
    /// JSON rather than as forty one columns, so adding a setting does not mean a
    /// migration.
    /// </remarks>
    public string SettingsJson { get; set; } = "{}";
}
