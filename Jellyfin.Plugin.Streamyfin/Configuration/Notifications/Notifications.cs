using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Notifications;


/// <summary>
/// Configuration for a notification
/// </summary>
public class NotificationConfiguration
{
    [Display(Name = "Enabled", Description = "Send the notifications for this event.")]
    [JsonPropertyName(name: "enabled")]
    public bool Enabled { get; set; }

    [Display(Name = "Recent event threshold", Description = "How long to wait, in seconds, before the same event can notify again.")]
    [JsonPropertyName(name: "recentEventThreshold")]
    public double? RecentEventThreshold { get; set; }
}

public class ItemAddedNotificationConfiguration: NotificationConfiguration
{
    [Display(Name = "Enabled libraries", Description = "The libraries whose new items get announced. Leave them all out to announce every library.")]
    [JsonPropertyName(name: "enabledLibraries")]
    public string[]? EnabledLibraries { get; set; }
}

public class Notifications
{
    [Display(Name = "Wording", Description = "Write any of the plugin's sentences differently, per language. Keep the placeholders the sentence has.")]
    [JsonPropertyName(name: "wording")]
    public List<WordingOverride>? Wording { get; set; }

    [NotNull]
    [Display(Name = "Session started", Description = "Admins get notified when a Jellyfin user comes online.")]
    [AboutSomebodyElse]
    [JsonPropertyName(name: "sessionStarted")]
    public NotificationConfiguration? SessionStarted { get; set; }

    [NotNull]
    [Display(Name = "Playback started", Description = "Admins get notified when a Jellyfin user starts playing something.")]
    [AboutSomebodyElse]
    [JsonPropertyName(name: "playbackStarted")]
    public NotificationConfiguration? PlaybackStarted { get; set; }

    [NotNull]
    [Display(Name = "User locked out", Description = "Admins, and the user themselves, get notified when Jellyfin locks an account.")]
    [AboutSomebodyElse]
    [JsonPropertyName(name: "userLockedOut")]
    public NotificationConfiguration? UserLockedOut { get; set; }

    [NotNull]
    [Display(Name = "Item added", Description = "Get notified when Jellyfin adds new movies or episodes.")]
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
    [AboutSomebodyElse]
    [JsonPropertyName(name: "signInFailed")]
    public NotificationConfiguration? SignInFailed { get; set; }
}