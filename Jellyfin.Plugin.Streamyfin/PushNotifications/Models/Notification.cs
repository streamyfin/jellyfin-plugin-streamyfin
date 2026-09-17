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

    /// <summary>
    /// The address of an image to show beside the text, such as a poster.
    /// </summary>
    /// <remarks>
    /// Whoever posts the notification decides where the image comes from, and it has to be
    /// an address the phone can fetch without credentials: Android fetches it as it is, and
    /// iOS asks for a notification service extension in the app. Anything that is not an
    /// http address is dropped rather than sent, since Expo refuses the whole message for
    /// it.
    /// </remarks>
    [JsonProperty(PropertyName = "image", NullValueHandling = NullValueHandling.Ignore)]
    public string? Image { get; set; }

    /// <summary>
    /// The same notification, in the shape Expo takes.
    /// </summary>
    /// <returns>The message to send.</returns>
    public ExpoNotificationRequest ToExpoNotification()
    {
        var image = DeviceServer.Stored(Image);

        return new ExpoNotificationRequest
        {
            Title = Title,
            Subtitle = Subtitle,
            Body = Body,
            RichContent = image is null ? null : new ExpoRichContent { Image = image }
        };
    }
}