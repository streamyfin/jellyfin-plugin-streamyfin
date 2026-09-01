using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Working out which push tokens are dead, which is the whole of P4.2 and the only part
/// of it that can delete something.
/// </summary>
/// <remarks>
/// Expo reports a dead token twice: once at send time, as an error ticket, and once
/// later through a receipt. The plugin listened to neither, so tokens accumulated
/// forever and every send to them silently went nowhere.
///
/// <para>
/// An error ticket carries no id and no token, so the only thing tying it back to a
/// device is its position: Expo answers with one ticket per recipient, in the order the
/// recipients were sent. Acting on that means a miscount deletes the wrong person's
/// token and their notifications stop with nothing to show why, which is worse than the
/// bug being fixed. Hence the count guard, and hence these tests.
/// </para>
/// </remarks>
public class ExpoReceiptTests
{
    private static TicketStatus Ok(string id) => new() { Status = "ok", Id = id };

    private static TicketStatus Failed(string error) => new()
    {
        Status = "error",
        Details = new TicketDetails { Error = error }
    };

    /// <summary>
    /// A ticket is matched to the recipient in the same position, so an error names the
    /// token it came from.
    /// </summary>
    [Fact]
    public void AnErrorTicketNamesTheTokenItCameFrom()
    {
        var outcome = ExpoTickets.Reconcile(
            ["token-a", "token-b", "token-c"],
            new ExpoNotificationResponse
            {
                Data = [Ok("ticket-a"), Failed(ExpoTickets.DeviceNotRegistered), Ok("ticket-c")]
            },
            null);

        Assert.Equal(["token-b"], outcome.DeadTokens);
        Assert.Equal(
            [("ticket-a", "token-a"), ("ticket-c", "token-c")],
            outcome.Pending.Select(p => (p.TicketId, p.Token)));
    }

    /// <summary>
    /// Fewer tickets than recipients means the positions no longer line up, and acting on
    /// them anyway would delete a token belonging to someone else. Nothing is pruned and
    /// nothing is queued.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void ACountThatDoesNotMatchPrunesNothing(int ticketCount)
    {
        var outcome = ExpoTickets.Reconcile(
            ["token-a", "token-b", "token-c"],
            new ExpoNotificationResponse
            {
                Data = Enumerable.Range(0, ticketCount)
                    .Select(i => i == 1 ? Failed(ExpoTickets.DeviceNotRegistered) : Ok($"ticket-{i}"))
                    .ToList()
            },
            null);

        Assert.Empty(outcome.DeadTokens);
        Assert.Empty(outcome.Pending);
    }

    /// <summary>
    /// Only <c>DeviceNotRegistered</c> means the token is gone. The other errors are about
    /// the message or the rate, and the device is still there.
    /// </summary>
    [Theory]
    [InlineData("MessageRateExceeded")]
    [InlineData("MessageTooBig")]
    [InlineData("MismatchSenderId")]
    [InlineData("InvalidCredentials")]
    [InlineData(null)]
    public void AnErrorThatIsNotDeviceNotRegisteredKeepsTheToken(string? error)
    {
        var outcome = ExpoTickets.Reconcile(
            ["token-a"],
            new ExpoNotificationResponse { Data = [Failed(error!)] },
            null);

        Assert.Empty(outcome.DeadTokens);

        // No id, so there is nothing to ask a receipt about either.
        Assert.Empty(outcome.Pending);
    }

    /// <summary>
    /// A refused send returns null, which is not evidence about anybody's token.
    /// </summary>
    [Fact]
    public void NoResponsePrunesNothing()
    {
        var outcome = ExpoTickets.Reconcile(["token-a"], null, null);

        Assert.Empty(outcome.DeadTokens);
        Assert.Empty(outcome.Pending);
    }

    /// <summary>
    /// The delivery can fail after the ticket said ok, which is the case the plugin never
    /// looked at: a receipt is the only place a token that died between the two is
    /// reported.
    /// </summary>
    [Fact]
    public void AReceiptSayingDeviceNotRegisteredKillsItsToken()
    {
        var dead = ExpoTickets.DeadTokensFrom(
            new ExpoReceiptResponse
            {
                Data = new Dictionary<string, TicketStatus>
                {
                    ["ticket-a"] = Ok("ticket-a"),
                    ["ticket-b"] = Failed(ExpoTickets.DeviceNotRegistered)
                }
            },
            new Dictionary<string, string>
            {
                ["ticket-a"] = "token-a",
                ["ticket-b"] = "token-b"
            });

        Assert.Equal(["token-b"], dead);
    }

    /// <summary>
    /// A receipt for a ticket this server did not send is not a reason to delete anything.
    /// Receipt ids come back from Expo, so a stale or duplicated one has to be inert.
    /// </summary>
    [Fact]
    public void AReceiptForAnUnknownTicketIsIgnored()
    {
        var dead = ExpoTickets.DeadTokensFrom(
            new ExpoReceiptResponse
            {
                Data = new Dictionary<string, TicketStatus>
                {
                    ["ticket-nobody-sent"] = Failed(ExpoTickets.DeviceNotRegistered)
                }
            },
            new Dictionary<string, string> { ["ticket-a"] = "token-a" });

        Assert.Empty(dead);
    }

    /// <summary>
    /// A receipt that has not resolved yet, or that failed for a reason about the message,
    /// leaves the token alone.
    /// </summary>
    [Fact]
    public void AReceiptThatIsNotAboutTheDeviceKeepsItsToken()
    {
        var dead = ExpoTickets.DeadTokensFrom(
            new ExpoReceiptResponse
            {
                Data = new Dictionary<string, TicketStatus>
                {
                    ["ticket-a"] = Failed("MessageRateExceeded")
                }
            },
            new Dictionary<string, string> { ["ticket-a"] = "token-a" });

        Assert.Empty(dead);
    }

    /// <summary>
    /// The same token can be queued more than once, from two notifications sent to the
    /// same device, and it should be reported dead once.
    /// </summary>
    [Fact]
    public void ATokenReportedDeadTwiceIsReportedOnce()
    {
        var dead = ExpoTickets.DeadTokensFrom(
            new ExpoReceiptResponse
            {
                Data = new Dictionary<string, TicketStatus>
                {
                    ["ticket-a"] = Failed(ExpoTickets.DeviceNotRegistered),
                    ["ticket-b"] = Failed(ExpoTickets.DeviceNotRegistered)
                }
            },
            new Dictionary<string, string>
            {
                ["ticket-a"] = "token-a",
                ["ticket-b"] = "token-a"
            });

        Assert.Equal(["token-a"], dead);
    }
}
