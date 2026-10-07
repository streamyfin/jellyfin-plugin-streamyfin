using System;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.Extensions;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// Turns a Seerr webhook into a notification, and decides who it is for.
/// </summary>
/// <remarks>
/// Without this an administrator writes the JSON template themselves, once per event,
/// in Seerr's webhook agent. That works and is documented in NOTIFICATIONS.md, but the
/// routing is the part a template cannot express: an approval belongs to the person who
/// asked, a failure belongs to whoever has to fix it.
///
/// <para>
/// Every event routes one of two ways and the split is the whole point. What the server
/// operator has to act on goes to administrators; what a user asked for goes back to
/// that user and to nobody else. An approval naming somebody is not something the rest
/// of the server should receive.
/// </para>
/// </remarks>
public class SeerrNotificationMapper
{
    private readonly LocalizationHelper _localization;
    private readonly ILogger<SeerrNotificationMapper> _logger;

    public SeerrNotificationMapper(
        LocalizationHelper localization,
        ILoggerFactory loggerFactory)
    {
        _localization = localization;
        _logger = loggerFactory.CreateLogger<SeerrNotificationMapper>();
    }

    /// <summary>
    /// Map a payload, or return null when the event is not one this handles.
    /// </summary>
    public Notification? Map(SeerrWebhookPayload? payload)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.NotificationType))
        {
            _logger.LogWarning("Seerr webhook carried no notification_type, ignoring it");
            return null;
        }

        var type = payload.NotificationType.ToUpperInvariant();

        // Issues keep the generic endpoint and the template in NOTIFICATIONS.md. They
        // are a conversation rather than a request lifecycle, they carry a comment body
        // this does not model, and an administrator who wants them already has a way.
        if (type.Contains("ISSUE", StringComparison.Ordinal))
        {
            _logger.LogDebug("Seerr sent {Type}, which is an issue event and not handled here", type.ForLog());
            return null;
        }

        var media = string.IsNullOrWhiteSpace(payload.Subject)
            ? _localization.GetString("SeerrUnknownMedia")
            : payload.Subject;

        var requester = payload.Request?.RequestedByUsername;

        var notification = type switch
        {
            "TEST_NOTIFICATION" or "TEST" => ForAdmins("SeerrTestTitle", "SeerrTestBody", requester, media, eventKey: null),
            "MEDIA_PENDING" => ForAdmins("SeerrRequestPendingTitle", "SeerrRequestPendingBody", requester, media, NotificationEvents.SeerrPending),
            "MEDIA_AUTO_APPROVED" => ForAdmins("SeerrRequestAutoApprovedTitle", "SeerrRequestAutoApprovedBody", requester, media, NotificationEvents.SeerrPending),
            "MEDIA_FAILED" => ForAdmins("SeerrRequestFailedTitle", "SeerrRequestFailedBody", requester, media, NotificationEvents.SeerrPending),
            "MEDIA_APPROVED" => ForRequester("SeerrRequestApprovedTitle", "SeerrRequestApprovedBody", requester, media),
            "MEDIA_DECLINED" => ForRequester("SeerrRequestDeclinedTitle", "SeerrRequestDeclinedBody", requester, media),
            "MEDIA_AVAILABLE" => ForRequester("SeerrRequestAvailableTitle", "SeerrRequestAvailableBody", requester, media),
            _ => Unknown(payload, requester)
        };

        _logger.LogDebug(
            "Seerr {Type} mapped to a notification for {Target}",
            type.ForLog(),
            notification.IsAdmin ? "administrators" : notification.Username.ForLog());

        return notification;
    }

    // The event a person turns these off by; none for Seerr's own test, which always arrives.
    private Notification ForAdmins(string titleKey, string bodyKey, string? requester, string media, string? eventKey) => new()
    {
        IsAdmin = true,
        EventKey = eventKey,
        Title = _localization.GetString(titleKey),
        Body = _localization.GetFormatted(bodyKey, args: [Requester(requester), media])
    };

    /// <summary>
    /// Aimed at the person who asked, and at nobody when Seerr named no requester.
    /// </summary>
    /// <remarks>
    /// A notification with no target and <c>IsAdmin</c> false is sent to every registered
    /// device by the endpoint. "Your request for X has been approved" reaching the whole
    /// server would be both wrong and a small leak, so an event with no requester goes to
    /// administrators instead: they are the ones who can find out whose it was.
    /// </remarks>
    private Notification ForRequester(string titleKey, string bodyKey, string? requester, string media)
    {
        if (string.IsNullOrWhiteSpace(requester))
        {
            _logger.LogWarning(
                "Seerr named no requester, so this went to administrators rather than to the whole server");

            return ForAdmins(titleKey, bodyKey, requester, media, NotificationEvents.SeerrPending);
        }

        return new Notification
        {
            Username = requester,
            EventKey = NotificationEvents.SeerrRequests,
            Title = _localization.GetString(titleKey),
            Body = _localization.GetFormatted(bodyKey, args: [requester, media])
        };
    }

    /// <summary>
    /// An event Seerr added after this was written. Seerr's own subject and message are
    /// already a sentence, so they are passed through rather than dropped.
    /// </summary>
    private Notification Unknown(SeerrWebhookPayload payload, string? requester)
    {
        _logger.LogWarning(
            "Seerr sent {Type}, which this does not know. Passing its own text through",
            payload.NotificationType.ForLog());

        if (string.IsNullOrWhiteSpace(requester))
        {
            return new Notification
            {
                IsAdmin = true,
                Title = payload.Subject,
                Body = payload.Message
            };
        }

        return new Notification
        {
            Username = requester,
            Title = payload.Subject,
            Body = payload.Message
        };
    }

    private string Requester(string? requester) =>
        string.IsNullOrWhiteSpace(requester) ? _localization.GetString("SeerrUnknownRequester") : requester;
}
