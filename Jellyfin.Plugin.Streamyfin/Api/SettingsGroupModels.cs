using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;

namespace Jellyfin.Plugin.Streamyfin.Api;

/// <summary>
/// A settings group, as the API returns it.
/// </summary>
/// <remarks>
/// Separate from the stored entity because the stored one keeps its settings as a
/// JSON string, and an administrator's client should receive settings rather than a
/// string containing settings.
/// </remarks>
public class SettingsGroupDto
{
    /// <summary>
    /// Gets or sets the group id. Absent when creating.
    /// </summary>
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    [Required]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets which group wins when a user is in more than one. Higher wins.
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    /// <summary>
    /// Gets or sets the settings this group overrides. Only the keys it means to
    /// change need to be present.
    /// </summary>
    [JsonPropertyName("settings")]
    public Configuration.Settings.Settings? Settings { get; set; }

    /// <summary>
    /// Gets or sets what this level says about the notifications its people get, by event
    /// key. Only the events it changes need to be present, and an event it does not name
    /// falls through to the level below.
    /// </summary>
    /// <remarks>
    /// Kept apart from the settings on purpose. The settings are served to the app; this
    /// is the server's own business, and mixing them would put it in front of every
    /// client.
    /// </remarks>
    [JsonPropertyName("notifications")]
    public Dictionary<string, NotificationTargeting>? Notifications { get; set; }

    /// <summary>
    /// Gets or sets the Jellyfin users in the group.
    /// </summary>
    [JsonPropertyName("userIds")]
    public List<Guid> UserIds { get; set; } = [];
}

/// <summary>
/// The settings an administrator targets at one user.
/// </summary>
public class UserSettingsOverrideDto
{
    /// <summary>
    /// Gets or sets the settings. Only the keys being overridden need to be present.
    /// </summary>
    [JsonPropertyName("settings")]
    public Configuration.Settings.Settings? Settings { get; set; }

    /// <summary>
    /// Gets or sets what this level says about the notifications its people get, by event
    /// key. Only the events it changes need to be present, and an event it does not name
    /// falls through to the level below.
    /// </summary>
    /// <remarks>
    /// Kept apart from the settings on purpose. The settings are served to the app; this
    /// is the server's own business, and mixing them would put it in front of every
    /// client.
    /// </remarks>
    [JsonPropertyName("notifications")]
    public Dictionary<string, NotificationTargeting>? Notifications { get; set; }
}

/// <summary>
/// Who is in a group.
/// </summary>
public class SettingsGroupMembersDto
{
    /// <summary>
    /// Gets or sets the Jellyfin users who should be in it afterwards.
    /// </summary>
    [Required]
    [JsonPropertyName("userIds")]
    public List<Guid> UserIds { get; set; } = [];
}
