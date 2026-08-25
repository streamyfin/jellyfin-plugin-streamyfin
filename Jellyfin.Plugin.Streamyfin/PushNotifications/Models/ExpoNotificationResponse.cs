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

public class TicketStatus
{
    [JsonProperty(PropertyName = "status")] //"error" | "ok",
    public string? Status { get; set; }

    [JsonProperty(PropertyName = "id")] 
    public string? Id { get; set; }

    [JsonProperty(PropertyName = "message")]
    public string? Message { get; set; }

    [JsonProperty(PropertyName = "details")]
    public object? Details { get; set; }
}

public class Errors
{
    [JsonProperty(PropertyName = "code")]
    public string? Code { get; set; }

    [JsonProperty(PropertyName = "message")]
    public string? Message { get; set; }
}