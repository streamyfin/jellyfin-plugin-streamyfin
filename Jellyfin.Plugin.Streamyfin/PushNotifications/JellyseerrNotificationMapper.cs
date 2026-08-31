using Jellyfin.Plugin.Streamyfin.PushNotifications.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// Maps Jellyseerr webhook notifications to Streamyfin notifications
/// </summary>
public class JellyseerrNotificationMapper
{
    private readonly LocalizationHelper _localizationHelper;
    private readonly ILogger<JellyseerrNotificationMapper> _logger;

    public JellyseerrNotificationMapper(
        LocalizationHelper localizationHelper,
        ILogger<JellyseerrNotificationMapper> logger)
    {
        _localizationHelper = localizationHelper;
        _logger = logger;
    }

    /// <summary>
    /// Convert Jellyseerr payload to Streamyfin notification
    /// </summary>
    /// <param name="payload">Jellyseerr webhook payload</param>
    /// <returns>Streamyfin notification or null if event should be ignored</returns>
    public Notification? MapToNotification(JellyseerrNotificationPayload payload)
    {
        if (payload == null || string.IsNullOrWhiteSpace(payload.NotificationType))
        {
            _logger.LogWarning("Invalid Jellyseerr payload: missing notification type");
            return null;
        }

        // Ignore issue-related events
        if (IsIssueEvent(payload.NotificationType))
        {
            _logger.LogDebug("Ignoring issue-related event: {0}", payload.NotificationType);
            return null;
        }

        var notificationType = payload.NotificationType.ToUpperInvariant();
        var mediaName = payload.Subject ?? "Unknown Media";
        var requestedBy = payload.Request?.RequestedByUsername ?? "Unknown User";

        _logger.LogDebug(
            "Processing Jellyseerr notification - Original type: '{0}', Normalized type: '{1}', Media: '{2}', Requested by: '{3}'",
            payload.NotificationType,
            notificationType,
            mediaName,
            requestedBy
        );

        var notification = new Notification
        {
            IsAdmin = false  // Explicitly set default value
        };

        switch (notificationType)
        {
            case "TEST":
            case "TEST_NOTIFICATION":
            case "MEDIA_PENDING":
                notification.IsAdmin = true;
                notification.Title = _localizationHelper.GetString("JellyseerrRequestPendingTitle");
                notification.Body = _localizationHelper.GetFormatted(
                    "JellyseerrRequestPendingBody",
                    args: new object[] { requestedBy, mediaName }
                );
                break;

            case "MEDIA_AUTO_APPROVED":
                notification.IsAdmin = true;
                notification.Title = _localizationHelper.GetString("JellyseerrRequestAutoApprovedTitle");
                notification.Body = _localizationHelper.GetFormatted(
                    "JellyseerrRequestAutoApprovedBody",
                    args: new object[] { requestedBy, mediaName }
                );
                break;

            case "MEDIA_FAILED":
                notification.IsAdmin = true;
                notification.Title = _localizationHelper.GetString("JellyseerrRequestFailedTitle");
                notification.Body = _localizationHelper.GetFormatted(
                    "JellyseerrRequestFailedBody",
                    args: new object[] { requestedBy, mediaName }
                );
                break;

            case "MEDIA_APPROVED":
                notification.Username = requestedBy;
                notification.Title = _localizationHelper.GetString("JellyseerrRequestApprovedTitle");
                notification.Body = _localizationHelper.GetFormatted(
                    "JellyseerrRequestApprovedBody",
                    args: new object[] { requestedBy, mediaName }
                );
                break;

            case "MEDIA_DECLINED":
                notification.Username = requestedBy;
                notification.Title = _localizationHelper.GetString("JellyseerrRequestDeclinedTitle");
                notification.Body = _localizationHelper.GetFormatted(
                    "JellyseerrRequestDeclinedBody",
                    args: new object[] { requestedBy, mediaName }
                );
                break;

            case "MEDIA_AVAILABLE":
                notification.Username = requestedBy;
                notification.Title = _localizationHelper.GetString("JellyseerrRequestAvailableTitle");
                notification.Body = _localizationHelper.GetFormatted(
                    "JellyseerrRequestAvailableBody",
                    args: new object[] { requestedBy, mediaName }
                );
                break;

            default:
                _logger.LogWarning("Unknown Jellyseerr notification type: '{0}', sending to requesting user with original content", notificationType);
                // Fallback to original subject and message, send to requesting user (safer than sending to all admins)
                notification.Title = payload.Subject;
                notification.Body = payload.Message;
                notification.Username = requestedBy;
                // Note: IsAdmin is already false from initialization
                break;
        }

        _logger.LogDebug(
            "Mapped Jellyseerr event '{0}' to notification - Title: '{1}', Body: '{2}', IsAdmin: {3}, Username: '{4}'",
            notificationType,
            notification.Title ?? "N/A",
            notification.Body ?? "N/A",
            notification.IsAdmin,
            notification.Username ?? "N/A"
        );

        return notification;
    }

    /// <summary>
    /// Check if the notification type is issue-related
    /// </summary>
    private bool IsIssueEvent(string notificationType)
    {
        var type = notificationType.ToUpperInvariant();
        return type.Contains("ISSUE");
    }
}
