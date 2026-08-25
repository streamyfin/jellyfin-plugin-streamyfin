using System;

namespace Jellyfin.Plugin.Streamyfin.Db;

/// <summary>
/// One Jellyfin user's membership of one <see cref="SettingsGroup"/>.
/// </summary>
/// <remarks>
/// A row rather than a list on the group, so a user's groups can be looked up
/// without reading every group. Membership is by Jellyfin user id: a user deleted
/// on the server leaves a row behind, which resolves to nothing and costs nothing.
/// </remarks>
public class SettingsGroupMember
{
    /// <summary>
    /// Gets or sets the group.
    /// </summary>
    public Guid GroupId { get; set; }

    /// <summary>
    /// Gets or sets the Jellyfin user.
    /// </summary>
    public Guid UserId { get; set; }
}
