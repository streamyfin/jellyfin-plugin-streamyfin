using System;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.models;

public class Notification
{
    /// <summary>
    /// Specific Jellyfin UserId that you want to target with this notification.
    /// This will attempt to notify all streamyfin clients that are logged in under this user.
    /// </summary>
    [JsonProperty(PropertyName = "userId")]
    public Guid? UserId { get; set; }
    
    /// <summary>
    /// Specific Jellyfin Username that you want to target with this notification.
    /// This will attempt to notify all streamyfin clients that are logged in under this username.
    /// </summary>
    [JsonProperty(PropertyName = "username")]
    public string? Username { get; set; }

    /// <summary>
    /// The title to display in the notification. Often displayed above the notification body.
    /// Maps to AndroidNotification.title and aps.alert.title
    /// </summary>
    [JsonProperty(PropertyName = "title", NullValueHandling = NullValueHandling.Ignore)]
    public string? Title { get; set; }

    /// <summary>
    /// iOS Only
    /// The subtitle to display in the notification below the title.
    /// Maps to aps.alert.subtitle.
    /// </summary>
    [JsonProperty(PropertyName = "subtitle")]
    public string? Subtitle { get; set; }

    /// <summary>
    /// The message to display in the notification.
    /// Maps to AndroidNotification.body and aps.alert.body.
    /// </summary>
    [JsonProperty(PropertyName = "body")]
    public string? Body { get; set; }
    
    /// <summary>
    /// Enforce that this notification is for Jellyfin admins only
    /// </summary>
    [JsonProperty(PropertyName = "isAdmin")]
    public bool IsAdmin { get; set; }

    public ExpoNotificationRequest ToExpoNotification() => new()
    {
        Title = Title,
        Subtitle = Subtitle,
        Body = Body
    };
}