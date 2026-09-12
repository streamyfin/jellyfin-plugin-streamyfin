using System;

namespace Jellyfin.Plugin.Streamyfin.Db;

/// <summary>
/// Settings an administrator targets at one user, above every group they are in.
/// </summary>
/// <remarks>
/// This is the third targeting level, and it is still the administrator speaking.
/// A user's own preferences live in the app, and what this level decides is what
/// the app is allowed to let them change.
/// </remarks>
public class UserSettingsOverride
{
    /// <summary>
    /// Gets or sets the Jellyfin user.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the settings targeted at them, as JSON.
    /// </summary>
    /// <remarks>
    /// A partial <c>Settings</c>, same shape as <see cref="SettingsGroup.SettingsJson"/>.
    /// </remarks>
    public string SettingsJson { get; set; } = "{}";
}
