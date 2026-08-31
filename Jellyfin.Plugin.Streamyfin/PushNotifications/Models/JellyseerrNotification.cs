using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Models;

/// <summary>
/// Jellyseerr webhook notification payload
/// </summary>
public class JellyseerrNotificationPayload
{
    [JsonPropertyName("notification_type")]
    public string? NotificationType { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("media")]
    public JellyseerrMedia? Media { get; set; }

    [JsonPropertyName("request")]
    public JellyseerrRequest? Request { get; set; }

    [JsonPropertyName("issue")]
    public JellyseerrIssue? Issue { get; set; }

    [JsonPropertyName("comment")]
    public JellyseerrComment? Comment { get; set; }

    [JsonPropertyName("extra")]
    public object[]? Extra { get; set; }
}

/// <summary>
/// Media information from Jellyseerr
/// </summary>
public class JellyseerrMedia
{
    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("tmdbId")]
    public string? TmdbId { get; set; }

    [JsonPropertyName("tvdbId")]
    public string? TvdbId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("status4k")]
    public string? Status4k { get; set; }
}

/// <summary>
/// Request information from Jellyseerr
/// </summary>
public class JellyseerrRequest
{
    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("requestedBy_email")]
    public string? RequestedByEmail { get; set; }

    [JsonPropertyName("requestedBy_username")]
    public string? RequestedByUsername { get; set; }

    [JsonPropertyName("requestedBy_avatar")]
    public string? RequestedByAvatar { get; set; }

    [JsonPropertyName("requestedBy_settings_discordId")]
    public string? RequestedBySettingsDiscordId { get; set; }

    [JsonPropertyName("requestedBy_settings_telegramChatId")]
    public string? RequestedBySettingsTelegramChatId { get; set; }
}

/// <summary>
/// Issue information from Jellyseerr
/// </summary>
public class JellyseerrIssue
{
    [JsonPropertyName("issue_id")]
    public string? IssueId { get; set; }

    [JsonPropertyName("issue_type")]
    public string? IssueType { get; set; }

    [JsonPropertyName("issue_status")]
    public string? IssueStatus { get; set; }

    [JsonPropertyName("reportedBy_email")]
    public string? ReportedByEmail { get; set; }

    [JsonPropertyName("reportedBy_username")]
    public string? ReportedByUsername { get; set; }

    [JsonPropertyName("reportedBy_avatar")]
    public string? ReportedByAvatar { get; set; }

    [JsonPropertyName("reportedBy_settings_discordId")]
    public string? ReportedBySettingsDiscordId { get; set; }

    [JsonPropertyName("reportedBy_settings_telegramChatId")]
    public string? ReportedBySettingsTelegramChatId { get; set; }
}

/// <summary>
/// Comment information from Jellyseerr
/// </summary>
public class JellyseerrComment
{
    [JsonPropertyName("comment_message")]
    public string? CommentMessage { get; set; }

    [JsonPropertyName("commentedBy_email")]
    public string? CommentedByEmail { get; set; }

    [JsonPropertyName("commentedBy_username")]
    public string? CommentedByUsername { get; set; }

    [JsonPropertyName("commentedBy_avatar")]
    public string? CommentedByAvatar { get; set; }

    [JsonPropertyName("commentedBy_settings_discordId")]
    public string? CommentedBySettingsDiscordId { get; set; }

    [JsonPropertyName("commentedBy_settings_telegramChatId")]
    public string? CommentedBySettingsTelegramChatId { get; set; }
}
