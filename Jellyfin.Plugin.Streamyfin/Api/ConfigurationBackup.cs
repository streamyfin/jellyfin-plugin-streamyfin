using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;

namespace Jellyfin.Plugin.Streamyfin.Api;

/// <summary>
/// Everything an administrator set, in one file.
/// </summary>
/// <remarks>
/// The configuration alone is not the work: the targeting levels are, and they live in
/// the plugin's own database rather than in Jellyfin's XML, so nothing a server
/// administrator backs up today carries them.
///
/// <para>
/// It carries the credentials the configuration carries, because a backup that cannot
/// restore a working server is not one. Both routes are for administrators, and the
/// page says so before it hands the file over.
/// </para>
/// </remarks>
public class ConfigurationBackup
{
    /// <summary>
    /// Gets or sets the plugin version that wrote this.
    /// </summary>
    [JsonPropertyName("plugin")]
    public string Plugin { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when it was taken.
    /// </summary>
    [JsonPropertyName("takenAt")]
    public DateTimeOffset TakenAt { get; set; }

    /// <summary>
    /// Gets or sets the configuration, which is everything the Yaml tab edits.
    /// </summary>
    [JsonPropertyName("config")]
    public Configuration.Config? Config { get; set; }

    /// <summary>
    /// Gets or sets the settings groups, with their members.
    /// </summary>
    [JsonPropertyName("groups")]
    public List<SettingsGroupDto> Groups { get; set; } = [];

    /// <summary>
    /// Gets or sets the settings targeted at one user each.
    /// </summary>
    [JsonPropertyName("users")]
    public List<UserBackup> Users { get; set; } = [];

    /// <summary>
    /// Gets or sets what each person chose for their own notifications, or <c>null</c> in a
    /// backup taken before they could, which a restore then leaves as they are.
    /// </summary>
    [JsonPropertyName("notificationPreferences")]
    public List<PreferencesBackup>? NotificationPreferences { get; set; }

    /// <summary>
    /// What makes the choices in this file impossible to restore, found before anything is
    /// written.
    /// </summary>
    /// <returns>A sentence for the page, or <c>null</c> when they can be restored.</returns>
    public string? PreferencesProblem() =>
        NotificationPreferences?.Any(choice => choice is null) == true
            ? "One of the notification choices in this file is empty."
            : null;
}

/// <summary>
/// The settings targeted at one user.
/// </summary>
public class UserBackup
{
    /// <summary>
    /// Gets or sets the Jellyfin user.
    /// </summary>
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets what is targeted at them.
    /// </summary>
    [JsonPropertyName("settings")]
    public Configuration.Settings.Settings? Settings { get; set; }
}

/// <summary>
/// One person's notification choices in a backup.
/// </summary>
public class PreferencesBackup
{
    /// <summary>
    /// Gets or sets the Jellyfin user.
    /// </summary>
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets their choices.
    /// </summary>
    [JsonPropertyName("preferences")]
    public NotificationPreferences? Preferences { get; set; }
}

/// <summary>
/// What a restore did.
/// </summary>
/// <remarks>
/// A backup taken on one server and restored on another names users that server has
/// never heard of. Those are skipped rather than refused, since the rest of the file is
/// still worth having, and counted so an administrator is told rather than left to
/// notice.
/// </remarks>
public class RestoreReport
{
    /// <summary>
    /// Gets or sets whether the configuration was replaced.
    /// </summary>
    [JsonPropertyName("configuration")]
    public bool Configuration { get; set; }

    /// <summary>
    /// Gets or sets how many groups were restored.
    /// </summary>
    [JsonPropertyName("groups")]
    public int Groups { get; set; }

    /// <summary>
    /// Gets or sets how many users were given their settings back.
    /// </summary>
    [JsonPropertyName("users")]
    public int Users { get; set; }

    /// <summary>
    /// Gets or sets how many group members this server does not have.
    /// </summary>
    [JsonPropertyName("unknownMembers")]
    public int UnknownMembers { get; set; }

    /// <summary>
    /// Gets or sets how many users the file targets that this server does not have.
    /// </summary>
    [JsonPropertyName("unknownUsers")]
    public int UnknownUsers { get; set; }

    /// <summary>
    /// Gets or sets how many people had their own notification choices put back.
    /// </summary>
    [JsonPropertyName("preferences")]
    public int Preferences { get; set; }

    /// <summary>
    /// Gets or sets what stopped the restore, when something did.
    /// </summary>
    [JsonPropertyName("problem")]
    public string? Problem { get; set; }
}
