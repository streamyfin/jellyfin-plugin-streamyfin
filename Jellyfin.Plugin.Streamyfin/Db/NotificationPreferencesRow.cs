using System;

namespace Jellyfin.Plugin.Streamyfin.Db;

/// <summary>
/// What one person keeps of their notifications (P4.5).
/// </summary>
/// <remarks>
/// Apart from <see cref="UserSettingsOverride"/>, which is the administrator speaking: clearing
/// what an administrator said about someone must leave what that person chose.
/// </remarks>
public class NotificationPreferencesRow
{
    /// <summary>
    /// Gets or sets the Jellyfin user.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the preferences, as JSON.
    /// </summary>
    public string PreferencesJson { get; set; } = "{}";

    /// <summary>
    /// Gets or sets when they were last saved, in UTC.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
