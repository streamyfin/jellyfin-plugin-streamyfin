using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Cutting a send into requests Expo accepts, and deciding when a refused one is worth
/// making again.
/// </summary>
public class ExpoBatchingTests
{
    /// <summary>
    /// A send within the limit is one request, unchanged.
    /// </summary>
    [Fact]
    public void ASmallSendIsOneRequest()
    {
        var batch = Assert.Single(ExpoBatching.Chunk([Addressed(3)]));

        Assert.Equal(3, Assert.Single(batch).To.Count);
    }

    /// <summary>
    /// A message with more recipients than a request takes is split, and every recipient
    /// goes out exactly once, in order.
    /// </summary>
    /// <remarks>
    /// Order is what <see cref="ExpoTickets.Reconcile"/> matches a ticket to a device by,
    /// so a shuffle here would delete the wrong token.
    /// </remarks>
    [Fact]
    public void ALargeMessageIsSplitInOrder()
    {
        var batches = ExpoBatching.Chunk([Addressed(250)]);

        Assert.Equal(3, batches.Count);
        Assert.Equal([100, 100, 50], batches.Select(Recipients).ToList());
        Assert.Equal(Tokens(250), batches.SelectMany(batch => batch.SelectMany(one => one.To)).ToList());
    }

    /// <summary>
    /// Several messages share a request until it is full, since Expo counts recipients
    /// across the whole body rather than per message.
    /// </summary>
    [Fact]
    public void MessagesShareARequestUntilItIsFull()
    {
        var batches = ExpoBatching.Chunk([Addressed(60), Addressed(60)]);

        Assert.Equal(2, batches.Count);
        Assert.Equal([100, 20], batches.Select(Recipients).ToList());

        // The second message is the one that straddles them, so it appears on both sides.
        Assert.Equal(2, batches[0].Count);
        Assert.Single(batches[1]);
    }

    /// <summary>
    /// A split copy keeps everything about the message except who it is for.
    /// </summary>
    /// <remarks>
    /// A field by field copy would drop a new field from every split send and nothing
    /// would fail, so the copy is shallow and this holds it that way.
    /// </remarks>
    [Fact]
    public void ASplitCopyKeepsTheMessage()
    {
        var notification = Addressed(150);
        notification.Title = "New episode";
        notification.Body = "Something arrived";
        notification.BadgeCount = 4;
        notification.ChannelId = "library";

        var first = ExpoBatching.Chunk([notification])[0][0];

        Assert.Equal("New episode", first.Title);
        Assert.Equal("Something arrived", first.Body);
        Assert.Equal(4, first.BadgeCount);
        Assert.Equal("library", first.ChannelId);
        Assert.Equal(100, first.To.Count);
    }

    /// <summary>
    /// A message addressed to nobody makes no request.
    /// </summary>
    [Fact]
    public void AMessageAddressedToNobodyMakesNoRequest()
    {
        Assert.Empty(ExpoBatching.Chunk([new ExpoNotificationRequest { Title = "Nobody" }]));
        Assert.Empty(ExpoBatching.Chunk([]));
    }

    /// <summary>
    /// A rate limit is worth waiting out; a bad request is not.
    /// </summary>
    /// <param name="status">What Expo answered.</param>
    /// <param name="retries">Whether the request is made again.</param>
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.RequestTimeout, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, false)]
    public void OnlyARefusalThatCanPassIsWaitedOut(HttpStatusCode status, bool retries)
    {
        Assert.Equal(retries, ExpoRetry.Default.Wait(status, null, 1, DateTimeOffset.UnixEpoch) is not null);
    }

    /// <summary>
    /// A send is only repeated when Expo says it did not take it. A read is repeated
    /// whenever it might have worked.
    /// </summary>
    /// <remarks>
    /// Expo's send endpoint carries no idempotency key and documents no deduplication,
    /// so a 500 or a timeout might mean the pushes went out and the answer was lost.
    /// Repeating that notifies everyone twice, which is worse than the notification
    /// being late. Asking what became of a ticket changes nothing, so there the same
    /// refusal is simply worth asking again.
    /// </remarks>
    /// <param name="status">What Expo answered.</param>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public void AnAmbiguousRefusalIsRepeatedForAReadAndNotForASend(HttpStatusCode status)
    {
        Assert.Null(ExpoRetry.Default.Wait(status, null, 1, DateTimeOffset.UnixEpoch));
        Assert.NotNull(ExpoRetry.Default.ForSomethingThatOnlyReads().Wait(status, null, 1, DateTimeOffset.UnixEpoch));

        // 429 stays safe either way: it is Expo saying it did not take the request.
        Assert.NotNull(ExpoRetry.Default.Wait(HttpStatusCode.TooManyRequests, null, 1, DateTimeOffset.UnixEpoch));
    }

    /// <summary>
    /// A policy that would wait a negative time is refused when it is built, rather than
    /// reaching Task.Delay to throw there.
    /// </summary>
    [Fact]
    public void ANegativeWaitIsRefusedWhenItIsBuilt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExpoRetry(3, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(30)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExpoRetry(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-30)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExpoRetry(0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30)));
    }

    /// <summary>
    /// The wait doubles, and giving up is what the last try does.
    /// </summary>
    [Fact]
    public void TheWaitDoublesAndThenStops()
    {
        var retry = new ExpoRetry(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(1), Waited(retry, 1));
        Assert.Equal(TimeSpan.FromSeconds(2), Waited(retry, 2));
        Assert.Null(retry.Wait(HttpStatusCode.TooManyRequests, null, 3, DateTimeOffset.UnixEpoch));
    }

    /// <summary>
    /// What the server asks for wins over the arithmetic, up to the cap.
    /// </summary>
    /// <remarks>
    /// A Retry-After of an hour is a real answer and still not something to sit through
    /// inside an event handler Jellyfin is waiting on.
    /// </remarks>
    [Fact]
    public void RetryAfterWinsButIsStillCapped()
    {
        var retry = new ExpoRetry(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

        Assert.Equal(
            TimeSpan.FromSeconds(5),
            retry.Wait(HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(TimeSpan.FromSeconds(5)), 1, DateTimeOffset.UnixEpoch));

        Assert.Equal(
            TimeSpan.FromSeconds(30),
            retry.Wait(HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(TimeSpan.FromHours(1)), 1, DateTimeOffset.UnixEpoch));
    }

    /// <summary>
    /// A Retry-After given as a date is read against the clock, and one already past is
    /// no wait at all rather than a negative one.
    /// </summary>
    [Fact]
    public void ARetryAfterDateIsReadAgainstTheClock()
    {
        var retry = new ExpoRetry(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
        var now = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            TimeSpan.FromSeconds(10),
            retry.Wait(HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(now.AddSeconds(10)), 1, now));

        Assert.Equal(
            TimeSpan.Zero,
            retry.Wait(HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(now.AddSeconds(-10)), 1, now));
    }

    private static TimeSpan? Waited(ExpoRetry retry, int tried) =>
        retry.Wait(HttpStatusCode.TooManyRequests, null, tried, DateTimeOffset.UnixEpoch);

    private static int Recipients(IReadOnlyList<ExpoNotificationRequest> batch) =>
        batch.Sum(notification => notification.To.Count);

    private static List<string> Tokens(int count) =>
        [.. Enumerable.Range(0, count).Select(i => $"ExponentPushToken[{i}]")];

    private static ExpoNotificationRequest Addressed(int recipients) =>
        new() { Title = "A title", To = Tokens(recipients) };
}
