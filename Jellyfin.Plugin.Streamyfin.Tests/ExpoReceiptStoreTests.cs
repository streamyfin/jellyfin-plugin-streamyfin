using System;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Db;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Storing the pushes Expo accepted, and removing the devices it later says are gone.
/// </summary>
/// <remarks>
/// The decision to delete is in <c>ExpoTickets</c> and tested there. This is the other
/// half: that the pair survives to the moment the receipt arrives, that the window has
/// both its ends, and that removing a token removes the right rows.
/// </remarks>
public class ExpoReceiptStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly PluginDatabase _db;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpoReceiptStoreTests"/> class.
    /// </summary>
    public ExpoReceiptStoreTests()
    {
        _directory = TestDirectory.Create();
        _db = new PluginDatabase(_directory);
    }

    private static DateTime Now => new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A push is only asked about once Expo has had time to produce a receipt. Asking
    /// straight away mostly answers nothing and spends the rate limit doing it.
    /// </summary>
    [Fact]
    public void OnlyPushesOldEnoughAreHandedBack()
    {
        _db.AddExpoReceipts([("ripe", "token-a")], Now.AddMinutes(-30));
        _db.AddExpoReceipts([("fresh", "token-b")], Now.AddMinutes(-1));

        var due = _db.GetExpoReceiptsSentBefore(Now.AddMinutes(-15), 100);

        Assert.Equal(["ripe"], due.Select(r => r.TicketId));
    }

    /// <summary>
    /// The oldest come first and no more than asked for, because Expo takes a thousand
    /// ticket ids per request and the ones closest to expiring are the ones about to be
    /// unanswerable.
    /// </summary>
    [Fact]
    public void TheOldestComeFirstAndTheBatchIsCapped()
    {
        for (var i = 0; i < 5; i++)
        {
            _db.AddExpoReceipts([($"ticket-{i}", $"token-{i}")], Now.AddHours(-5 + i));
        }

        var due = _db.GetExpoReceiptsSentBefore(Now, 3);

        Assert.Equal(["ticket-0", "ticket-1", "ticket-2"], due.Select(r => r.TicketId));
    }

    /// <summary>
    /// The token travels with the ticket rather than being looked up when the receipt
    /// arrives. By then the device may have registered a new token, and the row this
    /// ticket belonged to would no longer be the right one to delete.
    /// </summary>
    [Fact]
    public void TheTokenIsStoredBesideItsTicket()
    {
        _db.AddExpoReceipts([("ticket-a", "ExponentPushToken[abc]")], Now);

        var stored = Assert.Single(_db.GetExpoReceiptsSentBefore(Now, 100));

        Assert.Equal("ticket-a", stored.TicketId);
        Assert.Equal("ExponentPushToken[abc]", stored.Token);
        Assert.Equal(Now, stored.CreatedAt);
    }

    /// <summary>
    /// A ticket id already stored keeps the token it was first stored with. Expo does not
    /// reissue ids, so a collision means something is wrong upstream and the first row is
    /// the one that knows what it belonged to.
    /// </summary>
    [Fact]
    public void AKnownTicketKeepsItsFirstToken()
    {
        _db.AddExpoReceipts([("ticket-a", "token-first")], Now);
        _db.AddExpoReceipts([("ticket-a", "token-second")], Now);

        var stored = Assert.Single(_db.GetExpoReceiptsSentBefore(Now, 100));

        Assert.Equal("token-first", stored.Token);
    }

    /// <summary>
    /// A batch carrying the same ticket twice stores it once, rather than failing the
    /// whole insert on the primary key.
    /// </summary>
    [Fact]
    public void ATicketRepeatedInOneBatchIsStoredOnce()
    {
        _db.AddExpoReceipts([("ticket-a", "token-a"), ("ticket-a", "token-a")], Now);

        Assert.Single(_db.GetExpoReceiptsSentBefore(Now, 100));
    }

    /// <summary>
    /// Expo keeps a receipt for 24 hours. Past that the answer is never coming, and a row
    /// nothing ever removes is the same accumulation in a different table.
    /// </summary>
    [Fact]
    public void PushesExpoNoLongerAnswersForAreDropped()
    {
        _db.AddExpoReceipts([("old", "token-a")], Now.AddHours(-25));
        _db.AddExpoReceipts([("recent", "token-b")], Now.AddHours(-1));

        var dropped = _db.RemoveExpoReceiptsSentBefore(Now.AddHours(-24));

        Assert.Equal(1, dropped);
        Assert.Equal(["recent"], _db.GetExpoReceiptsSentBefore(Now, 100).Select(r => r.TicketId));
    }

    /// <summary>
    /// A device is forgotten by the token Expo named, since that is the only thing a
    /// receipt carries.
    /// </summary>
    [Fact]
    public void ADeviceIsForgottenByTheTokenExpoNamed()
    {
        _db.AddDeviceToken(new DeviceToken { DeviceId = Guid.NewGuid(), Token = "dead", UserId = Guid.NewGuid() });
        _db.AddDeviceToken(new DeviceToken { DeviceId = Guid.NewGuid(), Token = "alive", UserId = Guid.NewGuid() });

        var removed = _db.RemoveDeviceTokensNamed(["dead"]);

        Assert.Equal(1, removed);
        Assert.Equal(["alive"], _db.GetAllDeviceTokens().Select(t => t.Token));
    }

    /// <summary>
    /// One token can sit on more than one row, from a device that re-registered under a
    /// new id without the old row ever being cleaned up. That is the accumulation this
    /// part removes, so every match goes.
    /// </summary>
    [Fact]
    public void EveryRowCarryingADeadTokenGoes()
    {
        var userId = Guid.NewGuid();
        _db.AddDeviceToken(new DeviceToken { DeviceId = Guid.NewGuid(), Token = "dead", UserId = userId });
        _db.AddDeviceToken(new DeviceToken { DeviceId = Guid.NewGuid(), Token = "dead", UserId = userId });

        Assert.Equal(2, _db.RemoveDeviceTokensNamed(["dead"]));
        Assert.Empty(_db.GetAllDeviceTokens());
    }

    /// <summary>
    /// Nothing to remove removes nothing, and says so, rather than opening a write for a
    /// run where Expo reported everybody healthy.
    /// </summary>
    [Fact]
    public void NothingNamedRemovesNothing()
    {
        _db.AddDeviceToken(new DeviceToken { DeviceId = Guid.NewGuid(), Token = "alive", UserId = Guid.NewGuid() });

        Assert.Equal(0, _db.RemoveDeviceTokensNamed([]));
        Assert.Equal(0, _db.RemoveDeviceTokensNamed(["never-registered"]));
        Assert.Single(_db.GetAllDeviceTokens());
    }

    /// <summary>
    /// Collecting an answered receipt forgets it, so the next run does not ask again.
    /// </summary>
    [Fact]
    public void AnAnsweredPushIsForgotten()
    {
        _db.AddExpoReceipts([("ticket-a", "token-a"), ("ticket-b", "token-b")], Now);

        _db.RemoveExpoReceipts(["ticket-a"]);

        Assert.Equal(["ticket-b"], _db.GetExpoReceiptsSentBefore(Now, 100).Select(r => r.TicketId));
    }

    /// <summary>
    /// A backlog bigger than one batch is reached a batch at a time, and forgetting what
    /// was answered brings the rest into range.
    /// </summary>
    /// <remarks>
    /// A run that only ever took the first batch would let the oldest expire before
    /// anyone asked what became of them, which is the bug this part exists to fix.
    /// Sized past the batch the task uses on purpose, since that is also the number of
    /// ids one delete has to carry.
    /// </remarks>
    [Fact]
    public void ABacklogBiggerThanOneBatchIsReachedInPieces()
    {
        _db.AddExpoReceipts(
            Enumerable.Range(0, 1500).Select(i => ($"ticket-{i:D4}", $"token-{i}")),
            Now.AddHours(-1));

        var first = _db.GetExpoReceiptsSentBefore(Now, 1000);
        Assert.Equal(1000, first.Count);

        _db.RemoveExpoReceipts(first.Select(r => r.TicketId));

        var second = _db.GetExpoReceiptsSentBefore(Now, 1000);
        Assert.Equal(500, second.Count);
        Assert.Empty(second.Select(r => r.TicketId).Intersect(first.Select(r => r.TicketId)));
    }

    /// <summary>
    /// Skipping what has already been asked about happens in the query, before the limit,
    /// so a batch that went entirely unanswered does not hide everything behind it.
    /// </summary>
    /// <remarks>
    /// This is the difference between walking a backlog and starving on it. Filtered
    /// after the limit, a run whose first batch Expo did not resolve would take that same
    /// batch again, discard all of it, find nothing new and stop, and the rows behind it
    /// would wait until the old ones resolved or expired unread. The rows here all carry
    /// the same timestamp, which is what a burst of notifications actually produces.
    /// </remarks>
    [Fact]
    public void WhatWasAlreadyAskedIsSkippedBeforeTheLimit()
    {
        _db.AddExpoReceipts(
            Enumerable.Range(0, 1500).Select(i => ($"ticket-{i:D4}", $"token-{i}")),
            Now.AddHours(-1));

        // Asked about, and Expo answered for none of them, so every row is still stored.
        var first = _db.GetExpoReceiptsSentBefore(Now, 1000);
        var asked = first.Select(r => r.TicketId).ToHashSet(StringComparer.Ordinal);

        var second = _db.GetExpoReceiptsSentBefore(Now, 1000, asked);

        Assert.Equal(500, second.Count);
        Assert.Empty(second.Select(r => r.TicketId).Intersect(asked));
    }

    /// <summary>
    /// The exclusion holds at the size a full run actually reaches.
    /// </summary>
    /// <remarks>
    /// The task works through up to ten batches of a thousand, so by the last one it is
    /// asking the database to skip nine thousand ticket ids in a single query. SQLite has
    /// a ceiling on bound parameters, and a query built the wrong way would throw there
    /// and nowhere else: only on the busy servers this part exists to help, and only once
    /// the backlog is deep enough to reach the tenth batch.
    /// </remarks>
    [Fact]
    public void TheExclusionHoldsAtAFullRunsSize()
    {
        _db.AddExpoReceipts(
            Enumerable.Range(0, 10_000).Select(i => ($"ticket-{i:D5}", $"token-{i}")),
            Now.AddHours(-1));

        var asked = Enumerable.Range(0, 9_000)
            .Select(i => $"ticket-{i:D5}")
            .ToHashSet(StringComparer.Ordinal);

        var remaining = _db.GetExpoReceiptsSentBefore(Now, 1000, asked);

        Assert.Equal(1000, remaining.Count);
        Assert.Empty(remaining.Select(r => r.TicketId).Intersect(asked));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        TestDirectory.Delete(_directory);
    }
}
