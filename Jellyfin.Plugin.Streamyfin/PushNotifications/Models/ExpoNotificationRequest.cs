using System.Collections.Generic;
using Jellyfin.Plugin.Streamyfin.PushNotifications.enums;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.models;

/// <summary>
/// Expos Push Message format
/// see: https://exp.host/--/api/v2/push/send
/// </summary>
public class ExpoNotificationRequest
{
    /// <summary>
    /// An array of Expo push tokens specifying the recipient(s) of this message.
    /// </summary>
    [JsonProperty("to", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public List<string> To { get; set; } = [];

    /// <summary>
    /// iOS Only
    ///     When this is set to true,
    ///     the notification will cause the iOS app to start in the background to run a background task.
    ///     Your app needs to be configured to support this.
    ///     https://docs.expo.dev/versions/latest/sdk/notifications/#background-notifications
    ///     https://docs.expo.dev/versions/unversioned/sdk/notifications/#background-notification-configuration
    /// </summary>
    [JsonProperty("_contentAvailable", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool? ContentAvailable { get; set; }


    /// <summary>
    /// A JSON object delivered to your app. It may be up to about 4KiB;
    /// the total notification payload sent to Apple and Google must be at most 4KiB or else you will get a /
    /// "Message Too Big" error.
    /// </summary>
    [JsonProperty("data", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public object? Data { get; set; }

    /// <summary>
    /// The title to display in the notification. Often displayed above the notification body.
    /// Maps to AndroidNotification.title and aps.alert.title
    /// </summary>
    [JsonProperty(PropertyName = "title")]
    public string? Title { get; set; }

    /// <summary>
    /// The message to display in the notification.
    /// Maps to AndroidNotification.body and aps.alert.body.
    /// </summary>
    [JsonProperty(PropertyName = "body")]
    public string? Body { get; set; }

    /// <summary>
    /// The number of seconds for which the message may be kept around for redelivery if it hasn't been delivered yet.
    /// Defaults to null to use the respective defaults of each provider (1 month for Android/FCM as well as iOS/APNs).
    /// </summary>
    [JsonProperty(PropertyName = "ttl", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int? TimeToLive { get; set; } 

    /// <summary>
    /// Timestamp since the Unix epoch specifying when the message expires.
    /// Same effect as ttl (ttl takes precedence over expiration).
    /// </summary>
    [JsonProperty(PropertyName = "expiration", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int? Expiration { get; set; }

    /// <summary>
    /// 'default' | 'normal' | 'high'
    /// The delivery priority of the message.
    /// Specify default or omit this field to use the default priority on each platform /
    /// ("normal" on Android and "high" on iOS).
    /// </summary>
    [JsonProperty(PropertyName = "priority")] //'default' | 'normal' | 'high'
    public string Priority { get; set; } = "default";


    /// <summary>
    /// iOS Only
    /// The subtitle to display in the notification below the title.
    /// Maps to aps.alert.subtitle.
    /// </summary>
    [JsonProperty(PropertyName = "subtitle", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string? Subtitle { get; set; }

    /// <summary>
    /// iOS Only
    /// Play a sound when the recipient receives this notification.
    /// Specify default to play the device's default notification sound, or omit this field to play no sound.
    /// Custom sounds need to be configured via the config plugin and then specified including the file extension.
    /// Example: bells_sound.wav.
    /// </summary>
    [JsonProperty(PropertyName = "sound", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string? Sound { get; set; } = "default";

    /// <summary>
    /// iOS Only
    /// Number to display in the badge on the app icon.
    /// Specify zero to clear the badge.
    /// </summary>
    [JsonProperty(PropertyName = "badge", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int? BadgeCount { get; set; }

    /// <summary>
    /// iOS Only
    /// The importance and delivery timing of a notification.
    /// The string values correspond to the UNNotificationInterruptionLevel enumeration cases.
    /// https://developer.apple.com/documentation/usernotifications/unnotificationinterruptionlevel
    /// </summary>
    [JsonProperty(PropertyName = "interruptionLevel")]
    public InterruptionLevel InterruptionLevel { get; set; } = InterruptionLevel.active;

    /// <summary>
    /// Android Only
    /// ID of the Notification Channel through which to display this notification.
    /// If an ID is specified but the corresponding channel does not exist on the device /
    /// (that has not yet been created by your app), the notification will not be displayed to the user.
    /// </summary>
    [JsonProperty(PropertyName = "channelId", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string? ChannelId { get; set; }

    /// <summary>
    /// ID of the notification category that this notification is associated with.
    /// Find out more about notification categories here.
    /// https://docs.expo.dev/versions/latest/sdk/notifications/#manage-notification-categories-interactive-notifications
    /// </summary>
    [JsonProperty(PropertyName = "categoryId", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string? CategoryId { get; set; }

    /// <summary>
    /// Specifies whether this notification can be intercepted by the client app. Defaults to false.
    /// https://developer.apple.com/documentation/usernotifications/modifying_content_in_newly_delivered_notifications?language=objc
    /// </summary>
    [JsonProperty(PropertyName = "mutableContent")]
    public bool MutableContent { get; set; }
}

/// <summary>
/// Asking Expo for the receipts of tickets it handed back earlier.
/// see: https://exp.host/--/api/v2/push/getReceipts
/// </summary>
public class ExpoReceiptRequest
{
    /// <summary>
    /// Gets or sets the ticket ids to ask about. Expo takes a thousand per request.
    /// </summary>
    [JsonProperty("ids")]
    public List<string> Ids { get; set; } = [];
}
