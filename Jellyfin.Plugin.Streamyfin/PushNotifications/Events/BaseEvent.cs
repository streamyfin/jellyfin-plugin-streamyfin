using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;

public abstract class BaseEvent
{
    /// <summary>
    /// The events told about recently, each with when it was and how long it holds the next
    /// one back. Shared by every event, so each entry carries its own wait for the cleanup.
    /// </summary>
    internal static readonly ConcurrentDictionary<string, (DateTime Last, TimeSpan Wait)> RecentEvents = new();
    private static readonly TimeSpan CleanupThreshold = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CleanupEvery = TimeSpan.FromMinutes(1);

    // When the next cleanup may run, in UTC ticks.
    private static long _cleanupDue;

    protected static Config? Config => StreamyfinPlugin.Instance?.Settings.Current;
    
    protected readonly ILogger _logger;
    protected readonly LocalizationHelper _localization;
    protected readonly IServerApplicationHost _applicationHost;
    protected readonly NotificationHelper _notificationHelper;

    protected BaseEvent(
        ILoggerFactory loggerFactory,
        LocalizationHelper localization,
        IServerApplicationHost applicationHost,
        NotificationHelper notificationHelper)
    {
        _logger = loggerFactory.CreateLogger(GetType());
        _localization = localization;
        _applicationHost = applicationHost;
        _notificationHelper = notificationHelper;
    }

    /// <summary>
    /// Starts a send the caller cannot await, and logs the failure instead of
    /// letting it disappear.
    /// </summary>
    /// <param name="send">The send in progress.</param>
    /// <param name="what">What is being sent, for the log line.</param>
    /// <remarks>
    /// Jellyfin's event handlers are synchronous, so a send cannot be awaited from
    /// one. Before this the exception landed in a task nobody observed, which is
    /// why a failing send looked exactly like a working one.
    /// </remarks>
    protected void SendDetached(Task send, string what)
    {
        ArgumentNullException.ThrowIfNull(send);

        _ = Detached.Observe(send, e => _logger.LogError(e, "Failed to send the {What} notification", what));
    }

    /// <summary>
    /// Check if the event was recently processed before
    /// </summary>
    /// <param name="sessionKey"></param>
    /// <returns></returns>
    protected bool HasRecentlyProcessed(string sessionKey)
    {
        _logger.LogDebug("Checking recent events for key: {0}", sessionKey);

        var threshold = GetRecentEventThreshold();

        // Claimed rather than looked at and then written: two of the same event can arrive
        // on two threads, and a read followed by a write let both of them through, which is
        // exactly what this is here to stop. Whoever claims the key sends; the other is
        // told it is recent.
        while (true)
        {
            var now = DateTime.UtcNow;

            if (!RecentEvents.TryGetValue(sessionKey, out var last))
            {
                if (RecentEvents.TryAdd(sessionKey, (now, threshold)))
                {
                    _logger.LogDebug("No recent events for key: {0}", sessionKey);
                    return false;
                }

                continue;
            }

            if (now - last.Last < threshold)
            {
                _logger.LogDebug("There are recent events for key: {0}", sessionKey);
                return true;
            }

            if (RecentEvents.TryUpdate(sessionKey, (now, threshold), last))
            {
                _logger.LogDebug("No recent events for key: {0}", sessionKey);
                return false;
            }
        }
    }
    
    /// <summary>
    /// Forgets the events nobody waits on any more, at most once a minute.
    /// </summary>
    /// <remarks>
    /// Each of the events that wait runs it first, and it reads every remembered event, which
    /// stay as long as the longest wait in force: on a server open to the internet, refused sign
    /// ins from many addresses pile up, and every event paid for a full pass. An entry kept a
    /// minute longer changes nothing, as an event compares its own time to its wait.
    /// </remarks>
    protected void CleanupOldEntriesNowAndThen()
    {
        if (ClaimCleanup(DateTime.UtcNow))
        {
            CleanupOldEntries();
        }
    }

    /// <summary>
    /// Claims the next cleanup, once a minute has passed since the last one claimed.
    /// </summary>
    /// <param name="now">The time it is, in UTC.</param>
    /// <returns>Whether this caller runs the cleanup.</returns>
    internal static bool ClaimCleanup(DateTime now)
    {
        var due = Interlocked.Read(ref _cleanupDue);

        return now.Ticks >= due
            && Interlocked.CompareExchange(ref _cleanupDue, (now + CleanupEvery).Ticks, due) == due;
    }

    /// <summary>
    /// Forgets the events nobody waits on any more.
    /// </summary>
    /// <remarks>
    /// Each entry goes once its own wait and a margin have passed. The cleanup used the wait
    /// of the event that ran it, so a long wait on one event was cut short by another. The
    /// sum of a wait and the margin could overflow, so the elapsed time is compared to the
    /// wait first. An entry is removed only while it holds the value judged here: one claimed
    /// again since then is someone's fresh claim. An entry also stays while the longest wait in
    /// force covers it: forgotten at the wait it was sent under, an event whose wait was raised
    /// since could notify again before the new wait was over.
    /// </remarks>
    protected void CleanupOldEntries()
    {
        var now = DateTime.UtcNow;
        var longest = LongestWaitInForce();

        RecentEvents
            .Where(kvp =>
            {
                var keep = kvp.Value.Wait > longest ? kvp.Value.Wait : longest;
                var elapsed = now - kvp.Value.Last;
                return elapsed > keep && elapsed - keep > CleanupThreshold;
            })
            .ToList()
            .ForEach(kvp => RecentEvents.TryRemove(kvp));
    }

    /// <summary>
    /// How long two of the same event wait: as the server's configuration says, or as the
    /// event declares when it says nothing or has no block for the event at all.
    /// </summary>
    /// <param name="configuration">The event's configuration, which may be absent.</param>
    /// <param name="eventKey">The event, by the key the configuration uses.</param>
    /// <returns>The wait between two of the same event.</returns>
    protected static TimeSpan WaitFor(NotificationConfiguration? configuration, string eventKey) =>
        TimeSpan.FromSeconds(configuration?.RecentEventThreshold is { } seconds
            ? Math.Abs(seconds)
            : RecentEventWaitAttribute.SecondsFor(eventKey));

    /// <summary>
    /// The wait this event holds the next one back by, for the tests.
    /// </summary>
    internal TimeSpan WaitNow => GetRecentEventThreshold();

    /// <summary>
    /// The longest wait in force among the events that wait.
    /// </summary>
    /// <returns>The wait.</returns>
    protected virtual TimeSpan LongestWaitInForce() => SecondsAsWait(RecentEventWaitAttribute.LongestIn(Config?.notifications));

    // As long as the server can count, and none for a wait that is not a number.
    private static TimeSpan SecondsAsWait(double seconds) =>
        double.IsNaN(seconds) ? TimeSpan.Zero
        : seconds > NotificationsValidation.LongestWait ? TimeSpan.MaxValue
        : TimeSpan.FromSeconds(seconds);

    /// <summary>
    /// How long two of the same event wait. An event that waits says it, with a
    /// <see cref="RecentEventWaitAttribute"/> on its block so the Notifications tab offers it.
    /// </summary>
    /// <returns>The wait.</returns>
    /// <exception cref="InvalidOperationException">The event does not say how long it waits.</exception>
    /// <remarks>
    /// There was a 5 seconds fallback: an event that waited without saying so waited that long,
    /// with no field on the tab and nothing checking it.
    /// </remarks>
    protected virtual TimeSpan GetRecentEventThreshold() =>
        throw new InvalidOperationException($"{GetType().Name} waits between two of the same event without saying how long.");

}