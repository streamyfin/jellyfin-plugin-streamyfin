using System;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// Marks a message with its channel, thread and buttons, as far as its audience shows them.
/// </summary>
public static class MessageMarks
{
    /// <summary>
    /// Sets the fields an audience's devices can show.
    /// </summary>
    /// <param name="subject">What the message is about.</param>
    /// <param name="message">The message, changed in place.</param>
    /// <param name="audience">The devices it is written for.</param>
    public static void Apply(NotificationSubject subject, ExpoNotificationRequest message, Audience audience)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(audience);

        // A thread is only a hint, and a device that does not know it ignores it.
        message.ThreadId = subject.ThreadId;

        if (audience.Channels)
        {
            message.ChannelId = subject.Family;
        }

        if (audience.Categories)
        {
            message.CategoryId = subject.CategoryId;
        }
    }
}
