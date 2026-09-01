using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// Reading Expo's answers for what they say about the tokens they were sent to.
/// </summary>
/// <remarks>
/// Expo reports a dead token twice. At send time, as a ticket whose
/// <c>details.error</c> is <c>DeviceNotRegistered</c>. And later, through a receipt for
/// a ticket that had said ok, because a delivery can still fail after being accepted.
/// The plugin listened to neither, so a token stayed in the database after the app that
/// owned it was uninstalled, and every send to it went nowhere with nothing to show for
/// it.
///
/// <para>
/// Kept apart from <see cref="NotificationHelper"/> and free of the database on purpose.
/// This is the only code in the plugin that decides to delete something a user
/// registered, and it decides it from an assumption about ordering, so it is worth being
/// able to test on its own.
/// </para>
/// </remarks>
public static class ExpoTickets
{
    /// <summary>
    /// The one Expo error that means the token is gone rather than the message being
    /// wrong. Pruning on any other would delete a live device over a rate limit.
    /// </summary>
    public const string DeviceNotRegistered = "DeviceNotRegistered";

    private const string ErrorStatus = "error";

    /// <summary>
    /// Reads a send's answer: which of the recipients are dead, and which tickets are
    /// worth asking a receipt about later.
    /// </summary>
    /// <param name="recipients">The tokens the request was sent to, in order.</param>
    /// <param name="response">Expo's answer, or null when the send was refused.</param>
    /// <param name="logger">Where a mismatch is reported.</param>
    /// <returns>What to prune now and what to follow up.</returns>
    /// <remarks>
    /// An error ticket carries no id and no token. The only thing tying it back to a
    /// device is its position, since Expo answers with one ticket per recipient in the
    /// order they were sent. That assumption is load bearing and deleting the wrong
    /// token would stop a user's notifications with nothing to show why, so it is only
    /// acted on when the two counts agree exactly. They should always agree; the day
    /// they do not, doing nothing is the right answer.
    /// </remarks>
    public static TicketOutcome Reconcile(
        IReadOnlyList<string> recipients,
        ExpoNotificationResponse? response,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(recipients);

        var tickets = response?.Data;

        if (tickets is null || tickets.Count == 0)
        {
            return TicketOutcome.Nothing;
        }

        if (tickets.Count != recipients.Count)
        {
            logger?.LogWarning(
                "Expo answered with {Tickets} tickets for {Recipients} recipients, so a ticket cannot be "
                + "matched to the device it came from. Nothing is pruned this time",
                tickets.Count,
                recipients.Count);

            return TicketOutcome.Nothing;
        }

        var dead = new List<string>();
        var pending = new List<PendingReceipt>();

        for (var i = 0; i < tickets.Count; i++)
        {
            var ticket = tickets[i];

            if (IsGone(ticket))
            {
                dead.Add(recipients[i]);
                continue;
            }

            // Only an accepted ticket has an id, and only an id can be asked about later.
            if (!string.IsNullOrEmpty(ticket.Id))
            {
                pending.Add(new PendingReceipt { TicketId = ticket.Id, Token = recipients[i] });
            }
        }

        return new TicketOutcome(dead, pending);
    }

    /// <summary>
    /// Reads the receipts of tickets that were accepted, and names the tokens that have
    /// since been reported gone.
    /// </summary>
    /// <param name="response">Expo's answer, or null when the request was refused.</param>
    /// <param name="ticketToToken">The tokens the tickets were sent to, by ticket id.</param>
    /// <returns>The tokens to prune, each named once.</returns>
    /// <remarks>
    /// A receipt for a ticket this server did not send is ignored rather than trusted.
    /// The ids come back from Expo, and a stale or duplicated one must not be able to
    /// delete a token that is doing nothing wrong.
    /// </remarks>
    public static IReadOnlyList<string> DeadTokensFrom(
        ExpoReceiptResponse? response,
        IReadOnlyDictionary<string, string> ticketToToken)
    {
        ArgumentNullException.ThrowIfNull(ticketToToken);

        if (response?.Data is null)
        {
            return [];
        }

        return response.Data
            .Where(receipt => IsGone(receipt.Value))
            .Select(receipt => ticketToToken.TryGetValue(receipt.Key, out var token) ? token : null)
            .Where(token => token is not null)
            .Select(token => token!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsGone(TicketStatus ticket) =>
        string.Equals(ticket.Status, ErrorStatus, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ticket.Details?.Error, DeviceNotRegistered, StringComparison.Ordinal);
}

/// <summary>
/// A ticket Expo accepted, and the token it was accepted for.
/// </summary>
public class PendingReceipt
{
    /// <summary>
    /// Gets the ticket id, which is what a receipt is asked for by.
    /// </summary>
    public required string TicketId { get; init; }

    /// <summary>
    /// Gets the Expo push token the ticket was for.
    /// </summary>
    public required string Token { get; init; }
}

/// <summary>
/// What one send's answer said: what to delete now, and what to follow up later.
/// </summary>
public class TicketOutcome
{
    /// <summary>
    /// An answer that says nothing about anybody's token.
    /// </summary>
    public static readonly TicketOutcome Nothing = new([], []);

    /// <summary>
    /// Initializes a new instance of the <see cref="TicketOutcome"/> class.
    /// </summary>
    /// <param name="deadTokens">Tokens Expo reported as gone.</param>
    /// <param name="pending">Tickets worth asking a receipt about.</param>
    public TicketOutcome(IReadOnlyList<string> deadTokens, IReadOnlyList<PendingReceipt> pending)
    {
        DeadTokens = deadTokens;
        Pending = pending;
    }

    /// <summary>
    /// Gets the tokens to remove, reported dead at send time.
    /// </summary>
    public IReadOnlyList<string> DeadTokens { get; }

    /// <summary>
    /// Gets the tickets Expo accepted, to ask a receipt about once it has had time to
    /// deliver them.
    /// </summary>
    public IReadOnlyList<PendingReceipt> Pending { get; }
}
