using System;
using System.Net;
using System.Net.Http.Headers;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// When a refused Expo request is worth making again, and how long to wait first.
/// </summary>
/// <remarks>
/// Expo answers <c>429</c> past six hundred notifications a second for a project, and
/// the plugin treated that as a delivery that had nothing to report: the notification
/// was simply lost, with a line in the log nobody reads. A library that adds fifty
/// episodes at once is exactly the shape that hits it.
///
/// <para>
/// Only a refusal that can pass is retried. A <c>400</c> is the same request being
/// wrong twice, and repeating it costs a device nothing but costs the server two more
/// round trips inside an event handler.
/// </para>
/// </remarks>
public sealed class ExpoRetry
{
    /// <summary>
    /// What the plugin uses: three tries, a second apart, doubling, never past half a
    /// minute. The same shape as Expo's own server SDK, which retries twice with a
    /// factor of two from one second, and on the same one status.
    /// </summary>
    public static readonly ExpoRetry Default = new(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

    /// <summary>
    /// Never wait, for a test that would otherwise sleep through its own retries.
    /// </summary>
    public static readonly ExpoRetry Immediate = new(3, TimeSpan.Zero, TimeSpan.Zero);

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpoRetry"/> class.
    /// </summary>
    /// <param name="tries">How many times the request is made in total, the first included.</param>
    /// <param name="firstWait">The wait before the second try.</param>
    /// <param name="longestWait">The cap, which a server's own Retry-After is held to as well.</param>
    /// <param name="repeatWhenTheAnswerIsAmbiguous">
    /// Whether a refusal that does not say the request was dropped is worth repeating.
    /// False for anything that changes something, true for a read.
    /// </param>
    public ExpoRetry(
        int tries,
        TimeSpan firstWait,
        TimeSpan longestWait,
        bool repeatWhenTheAnswerIsAmbiguous = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tries, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(firstWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(longestWait, TimeSpan.Zero);

        Tries = tries;
        FirstWait = firstWait;
        LongestWait = longestWait;
        RepeatWhenTheAnswerIsAmbiguous = repeatWhenTheAnswerIsAmbiguous;
    }

    /// <summary>
    /// Gets how many times the request is made in total.
    /// </summary>
    public int Tries { get; }

    /// <summary>
    /// Gets the wait before the second try, doubled for each one after it.
    /// </summary>
    public TimeSpan FirstWait { get; }

    /// <summary>
    /// Gets the longest this will wait, whatever the arithmetic or the server says.
    /// </summary>
    public TimeSpan LongestWait { get; }

    /// <summary>
    /// Gets whether a refusal that does not say the request was dropped is repeated.
    /// </summary>
    /// <remarks>
    /// A send is not something to repeat on a maybe. Expo's send endpoint carries no
    /// idempotency key and documents no deduplication, so a 500 or a timeout might mean
    /// the pushes went out and the answer was lost, and repeating it would notify
    /// everyone twice. A 429 is different: it is the server saying in so many words
    /// that it did not take the request.
    ///
    /// <para>
    /// Reading a receipt changes nothing, so for that a 5xx is simply worth asking
    /// again. Expo's own server SDK retries on 429 and on nothing else, for either.
    /// </para>
    /// </remarks>
    public bool RepeatWhenTheAnswerIsAmbiguous { get; }

    /// <summary>
    /// The same policy, for a request that can be repeated without consequence.
    /// </summary>
    /// <returns>The policy, repeating on an ambiguous refusal as well.</returns>
    public ExpoRetry ForSomethingThatOnlyReads() =>
        RepeatWhenTheAnswerIsAmbiguous ? this : new ExpoRetry(Tries, FirstWait, LongestWait, true);

    /// <summary>
    /// How long to wait before trying again, or <c>null</c> to give up.
    /// </summary>
    /// <param name="status">The status Expo answered with.</param>
    /// <param name="retryAfter">The <c>Retry-After</c> header, when there was one.</param>
    /// <param name="tried">How many times the request has been made, the first being 1.</param>
    /// <param name="now">The clock, for a <c>Retry-After</c> given as a date.</param>
    /// <returns>The wait, or <c>null</c> when there is no point.</returns>
    public TimeSpan? Wait(HttpStatusCode status, RetryConditionHeaderValue? retryAfter, int tried, DateTimeOffset now)
    {
        if (tried >= Tries || !CanPass(status))
        {
            return null;
        }

        // The server's own answer wins over the arithmetic, which is guessing, but it is
        // still held to the cap: a Retry-After of an hour inside an event handler is not
        // something to sit through.
        var asked = Asked(retryAfter, now);
        if (asked is not null)
        {
            return Capped(asked.Value);
        }

        var doublings = Math.Min(tried - 1, 16);

        return Capped(FirstWait * Math.Pow(2, doublings));
    }

    // 429 is the one this exists for, and the only one that is safe whatever the
    // request was: Expo is saying it did not take it. A 5xx or a 408 might mean it did
    // and the answer was lost, which for a send would mean notifying everyone twice.
    private bool CanPass(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests
        || (RepeatWhenTheAnswerIsAmbiguous
            && (status == HttpStatusCode.RequestTimeout || (int)status >= 500));

    private static TimeSpan? Asked(RetryConditionHeaderValue? retryAfter, DateTimeOffset now)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - now;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    private TimeSpan Capped(TimeSpan wait) =>
        wait < TimeSpan.Zero ? TimeSpan.Zero : (wait > LongestWait ? LongestWait : wait);
}
