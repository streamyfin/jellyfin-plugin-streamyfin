using System.Collections.Generic;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.models;

public class ExpoNotificationResponse
{
    [JsonProperty(PropertyName = "data")]
    public List<TicketStatus> Data { get; set; } = [];

    [JsonProperty(PropertyName = "errors")]
    public List<Errors> Errors { get; set; } = [];
}

/// <summary>
/// What Expo answers when asked for the receipts of tickets it accepted earlier.
/// </summary>
/// <remarks>
/// Keyed by ticket id rather than positional, because a receipt can be asked for long
/// after the send and Expo only keeps them for 24 hours: an id that has expired, or was
/// never ours, is simply absent from the answer.
/// </remarks>
public class ExpoReceiptResponse
{
    /// <summary>
    /// Gets or sets the receipts, by the ticket id they answer for.
    /// </summary>
    [JsonProperty(PropertyName = "data")]
    public Dictionary<string, TicketStatus> Data { get; set; } = [];

    /// <summary>
    /// Gets or sets the errors about the request itself, rather than about a delivery.
    /// </summary>
    [JsonProperty(PropertyName = "errors")]
    public List<Errors> Errors { get; set; } = [];
}

public class TicketStatus
{
    [JsonProperty(PropertyName = "status")] //"error" | "ok",
    public string? Status { get; set; }

    [JsonProperty(PropertyName = "id")]
    public string? Id { get; set; }

    [JsonProperty(PropertyName = "message")]
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets what went wrong, when something did.
    /// </summary>
    /// <remarks>
    /// Typed rather than <c>object</c>, because <c>details.error</c> is the only place
    /// Expo says a token is dead and the plugin has to read it to prune. Nothing read
    /// this field before it was given a shape.
    /// </remarks>
    [JsonProperty(PropertyName = "details")]
    public TicketDetails? Details { get; set; }
}

/// <summary>
/// The machine readable half of a failed ticket or receipt.
/// </summary>
public class TicketDetails
{
    /// <summary>
    /// Gets or sets Expo's error code, such as <c>DeviceNotRegistered</c>.
    /// </summary>
    [JsonProperty(PropertyName = "error")]
    public string? Error { get; set; }
}

public class Errors
{
    [JsonProperty(PropertyName = "code")]
    public string? Code { get; set; }

    [JsonProperty(PropertyName = "message")]
    public string? Message { get; set; }
}
