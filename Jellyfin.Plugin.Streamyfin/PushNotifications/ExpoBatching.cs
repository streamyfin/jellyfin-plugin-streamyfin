using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// Cutting a send into requests Expo will accept.
/// </summary>
/// <remarks>
/// Expo takes a hundred recipients per request, counted across the whole body rather
/// than per message, and answers <c>413</c> past that. The plugin sent every device on
/// the server in one request: a library addition on a server with more than a hundred
/// registered devices was refused whole, and nobody was notified rather than everybody.
///
/// <para>
/// Order is the reason this is its own file. <see cref="ExpoTickets.Reconcile"/> matches
/// a ticket to a device by position and nothing else, so a batch has to carry its
/// recipients in the order they went out, and a message split across two batches has to
/// keep the tokens on the side they were sent from.
/// </para>
/// </remarks>
public static class ExpoBatching
{
    /// <summary>
    /// How many recipients Expo takes in one send, across every message in the body.
    /// </summary>
    /// <remarks>
    /// The number Expo's own server SDK batches on, and the one its documentation
    /// states for a request body.
    /// </remarks>
    public const int MaxRecipientsPerRequest = 100;

    /// <summary>
    /// Cuts a send into requests, each within Expo's recipient limit.
    /// </summary>
    /// <param name="notifications">The messages to send, each with its recipients.</param>
    /// <param name="limit">The recipients one request may carry.</param>
    /// <returns>The requests to make, in order. Empty when there is nobody to send to.</returns>
    public static IReadOnlyList<IReadOnlyList<ExpoNotificationRequest>> Chunk(
        IReadOnlyList<ExpoNotificationRequest> notifications,
        int limit = MaxRecipientsPerRequest)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var batches = new List<IReadOnlyList<ExpoNotificationRequest>>();
        var batch = new List<ExpoNotificationRequest>();
        var room = limit;

        foreach (var notification in notifications)
        {
            var recipients = notification?.To;
            if (recipients is null || recipients.Count == 0)
            {
                continue;
            }

            var sent = 0;

            while (sent < recipients.Count)
            {
                if (room == 0)
                {
                    batches.Add(batch);
                    batch = [];
                    room = limit;
                }

                var take = Math.Min(room, recipients.Count - sent);

                batch.Add(notification!.WithRecipients(recipients.GetRange(sent, take)));
                sent += take;
                room -= take;
            }
        }

        if (batch.Count > 0)
        {
            batches.Add(batch);
        }

        return batches;
    }
}
