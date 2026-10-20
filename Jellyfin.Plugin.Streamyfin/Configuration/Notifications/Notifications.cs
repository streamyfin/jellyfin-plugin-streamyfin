using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Notifications;


/// <summary>
/// Configuration for a notification
/// </summary>
public class NotificationConfiguration
{
    [Display(Name = "Enabled", Description = "Send the notifications for this event.")]
    [JsonPropertyName(name: "enabled")]
    public bool Enabled { get; set; }

    [Display(Name = "Recent event threshold", Description = "How long to wait, in seconds, before the same event can notify again. Only a session started, a playback started, a failed task, a changed plugin and a refused sign in wait; the other events ignore it.")]
    [JsonPropertyName(name: "recentEventThreshold")]
    public double? RecentEventThreshold { get; set; }
}

public class ItemAddedNotificationConfiguration: NotificationConfiguration
{
    /// <summary>
    /// How long, in seconds, a new item message waits for its title's id when nothing says
    /// otherwise (#289).
    /// </summary>
    public const double UsualWaitForTitleId = 600;

    /// <summary>
    /// The longest wait for a title's id, in seconds: a message held longer is not news any more.
    /// </summary>
    public const double LongestWaitForTitleId = 3600;

    [Display(Name = "Enabled libraries", Description = "The libraries whose new items get announced. Leave them all out to announce every library.")]
    [JsonPropertyName(name: "enabledLibraries")]
    public string[]? EnabledLibraries { get; set; }

    [Display(Name = "Wait for the title's id", Description = "While somebody waits for a title, how long, in seconds, a new movie or batch of episodes waits for Jellyfin to find its TMDB or TVDB id, so the people who waited for it get only the awaited title message. It goes as soon as the id comes. 0 sends at once. Ten minutes when left empty, an hour at most.")]
    [Bounds(0, LongestWaitForTitleId)]
    [JsonPropertyName(name: "waitForTitleId")]
    public double? WaitForTitleId { get; set; }
}

public class Notifications
{
    [Display(Name = "Wording", Description = "Write any of the plugin's sentences differently, per language. Keep the placeholders the sentence has.")]
    [JsonPropertyName(name: "wording")]
    public List<WordingOverride>? Wording { get; set; }

    [NotNull]
    [Display(Name = "Session started", Description = "Admins get notified when a Jellyfin user comes online.")]
    [AboutSomebodyElse]
    [RecentEventWait(RecentEventWaitAttribute.Usual)]
    [JsonPropertyName(name: "sessionStarted")]
    public NotificationConfiguration? SessionStarted { get; set; }

    [NotNull]
    [Display(Name = "Playback started", Description = "Admins get notified when a Jellyfin user starts playing something.")]
    [AboutSomebodyElse]
    [RecentEventWait(RecentEventWaitAttribute.Usual)]
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
    [Display(Name = "Awaited title", Description = "People get notified when a title they asked to be told about arrives, even with new items or its library turned off, unless they paused their notifications.")]
    [JsonPropertyName(name: "awaitedTitle")]
    public NotificationConfiguration? AwaitedTitle { get; set; }

    [NotNull]
    [Display(Name = "Scheduled task failed", Description = "Admins get notified when one of the server's scheduled tasks fails, with the reason it gave.")]
    [RecentEventWait(RecentEventWaitAttribute.Usual)]
    [JsonPropertyName(name: "taskFailed")]
    public NotificationConfiguration? TaskFailed { get; set; }

    [NotNull]
    [Display(Name = "Plugin changed", Description = "Admins get notified when a plugin is installed, updated or uninstalled.")]
    [RecentEventWait(RecentEventWaitAttribute.Usual)]
    [JsonPropertyName(name: "pluginChanged")]
    public NotificationConfiguration? PluginChanged { get; set; }

    [NotNull]
    [Display(Name = "Failed sign in", Description = "Admins get notified when a sign in is refused, with the name that was tried and where it came from. Notifications about the same address wait five minutes by default.")]
    [AboutSomebodyElse]
    [RecentEventWait(RecentEventWaitAttribute.SignInFailed)]
    [JsonPropertyName(name: "signInFailed")]
    public NotificationConfiguration? SignInFailed { get; set; }
}