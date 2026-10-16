using System;

namespace Jellyfin.Plugin.Streamyfin.Db;

/// <summary>
/// A push Expo accepted, waiting for the receipt that says whether it was delivered.
/// </summary>
/// <remarks>
/// Expo answers a send with a ticket, which only means the message was queued. Whether
/// it reached the device, and in particular whether the device is gone, is only in the
/// receipt, which becomes available minutes later and is kept for 24 hours. So the pair
/// has to outlive the request, and a server restart, or the answer is lost.
///
/// <para>
/// The token is stored beside the ticket rather than looked up afterwards, because by
/// the time the receipt arrives the device may have registered a new token and the row
/// this ticket belonged to would no longer be the right one to delete.
/// </para>
/// </remarks>
public class ExpoReceipt
{
    /// <summary>
    /// Gets or sets the ticket id Expo returned, which the receipt is asked for by.
    /// </summary>
    public string TicketId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Expo push token the ticket was sent to.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the send happened, in UTC.
    /// </summary>
    /// <remarks>
    /// Drives both ends of the window: a receipt is not asked for before Expo has had
    /// time to produce one, and a row is dropped once Expo would no longer have it.
    /// </remarks>
    public DateTime CreatedAt { get; set; }
}
