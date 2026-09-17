using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Notifications;


/// <summary>
/// Configuration for a notification
/// </summary>
public class NotificationConfiguration
{
    [Display(Name = "Enabled", Description = "if true, the notifications for this event are enabled.")]
    [JsonPropertyName(name: "enabled")]
    public bool Enabled { get; set; }

    [Display(Name = "Recent event threshold", Description = "How long we want to wait until allowing a duplicate event from being processed in seconds")]
    [JsonPropertyName(name: "recentEventThreshold")]
    public double? RecentEventThreshold { get; set; }
}

public class ItemAddedNotificationConfiguration: NotificationConfiguration
{
    [Display(Name = "Enabled libraries", Description = "Enter all library Ids you want to receive notifications from. Leave it out to notify for every library.")]
    [JsonPropertyName(name: "enabledLibraries")]
    public string[]? EnabledLibraries { get; set; }
}

public class UserNotificationConfig : NotificationConfiguration
{
    [Display(Name = "Jellyfin User Ids", Description = "List of jellyfin user ids that this notification is for.")]
    [JsonPropertyName(name: "userIds")]
    public string[]? UserIds { get; set; }

    [Display(Name = "Jellyfin Usernames", Description = "List of jellyfin usernames that this notification is for.")]
    [JsonPropertyName(name: "usernames")]
    public string[]? Usernames { get; set; }

    [Display(Name = "Forward to admins", Description = "if true, the notification will be forwarded to admins alongside any defined users.")]
    [JsonPropertyName(name: "forwardToAdmins")]
    public bool ForwardToAdmins { get; set; }
}

public class Notifications
{
    [NotNull]
    [Display(Name = "Session Started", Description = "Admins get notified when a jellyfin user is online.")]
    [JsonPropertyName(name: "sessionStarted")]
    public NotificationConfiguration? SessionStarted { get; set; }

    [NotNull]
    [Display(Name = "Playback Started", Description = "Admins get notified when a jellyfin user is starts playback.")]
    [JsonPropertyName(name: "playbackStarted")]
    public NotificationConfiguration? PlaybackStarted { get; set; }

    [NotNull]
    [Display(Name = "User locked out", Description = "Admins and locked out user get notified jellyfin locks their account")]
    [JsonPropertyName(name: "userLockedOut")]
    public NotificationConfiguration? UserLockedOut { get; set; }

    [NotNull]
    [Display(Name = "Item added", Description = "Get notified when jellyfin adds new Movies or Episodes")]
    [JsonPropertyName(name: "itemAdded")]
    public ItemAddedNotificationConfiguration? ItemAdded { get; set; }

    [NotNull]
    [Display(Name = "Scheduled task failed", Description = "Admins get notified when one of the server's scheduled tasks fails, with the reason it gave.")]
    [JsonPropertyName(name: "taskFailed")]
    public NotificationConfiguration? TaskFailed { get; set; }

    [NotNull]
    [Display(Name = "Plugin changed", Description = "Admins get notified when a plugin is installed, updated or uninstalled.")]
    [JsonPropertyName(name: "pluginChanged")]
    public NotificationConfiguration? PluginChanged { get; set; }

    [NotNull]
    [Display(Name = "Failed sign in", Description = "Admins get notified when a sign in is refused, with the name that was tried and where it came from. Notifications about the same address wait five minutes by default.")]
    [JsonPropertyName(name: "signInFailed")]
    public NotificationConfiguration? SignInFailed { get; set; }
}