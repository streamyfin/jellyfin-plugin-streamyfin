using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.models;

/// <summary>
/// What Seerr posts to its webhook agent.
/// </summary>
/// <remarks>
/// The shape is Seerr's, so the property names are not ours to choose. It mixes snake
/// case and camel case in the same object because Overseerr's template variables did,
/// and Seerr kept them for compatibility.
///
/// <para>
/// Only the fields the mapper reads are declared. Everything else Seerr sends is
/// ignored rather than modelled, which is deliberate: an unknown field must not fail
/// deserialization when Seerr adds one.
/// </para>
/// </remarks>
public class SeerrWebhookPayload
{
    /// <summary>The event, such as <c>MEDIA_APPROVED</c>. Everything routes on this.</summary>
    [JsonPropertyName("notification_type")]
    public string? NotificationType { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    /// <summary>The media title, which is what a notification is about.</summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("request")]
    public SeerrRequest? Request { get; set; }
}

/// <summary>
/// The request an event is about, when there is one.
/// </summary>
/// <remarks>
/// Seerr also sends the requester's email, avatar, Discord id and Telegram chat id.
/// None of them are declared here. The mapper needs a Jellyfin username to route on and
/// nothing else, and a field that is never read is a field that cannot end up in a log.
/// </remarks>
public class SeerrRequest
{
    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    /// <summary>
    /// Who asked for it. This is matched against Jellyfin usernames, so it only routes
    /// when the Seerr account was signed in through Jellyfin, which is the normal case
    /// for a Seerr sitting in front of one.
    /// </summary>
    [JsonPropertyName("requestedBy_username")]
    public string? RequestedByUsername { get; set; }
}
