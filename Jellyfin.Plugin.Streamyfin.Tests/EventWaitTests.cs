using System;
using System.Runtime.CompilerServices;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// How long two of the same event wait, as the events do it rather than as the tabs say it.
/// </summary>
public class EventWaitTests
{
    /// <summary>
    /// An event waits what the Notifications tab says when nothing sets a wait, read from the
    /// same attribute. One whose block is missing from the configuration does too, where the
    /// session and playback events threw instead and the notification was lost.
    /// </summary>
    [Theory]
    [InlineData(typeof(SessionStartEvent), "sessionStarted")]
    [InlineData(typeof(PlaybackStartEvent), "playbackStarted")]
    [InlineData(typeof(TaskFailedService), "taskFailed")]
    [InlineData(typeof(PluginChangedEvent), "pluginChanged")]
    [InlineData(typeof(SignInFailedEvent), "signInFailed")]
    public void AnEventWaitsWhatItsAttributeSays(Type eventType, string eventKey)
    {
        // Built without its services: the wait only reads the configuration, and with no
        // plugin loaded there is none, as on a server whose configuration has no block for it.
        var built = (BaseEvent)RuntimeHelpers.GetUninitializedObject(eventType);

        Assert.Equal(TimeSpan.FromSeconds(RecentEventWaitAttribute.SecondsFor(eventKey)), built.WaitNow);
    }

    /// <summary>
    /// Each event reads its own block, and a wait set there wins over the default, made
    /// positive as the events always made it. Every event has a different wait here, so one
    /// reading another's block, or another's default, cannot pass.
    /// </summary>
    [Theory]
    [InlineData(typeof(SessionStartEvent), 11)]
    [InlineData(typeof(PlaybackStartEvent), 12)]
    [InlineData(typeof(TaskFailedService), 13)]
    [InlineData(typeof(PluginChangedEvent), 14)]
    [InlineData(typeof(SignInFailedEvent), 15)]
    public void AnEventReadsItsOwnWait(Type eventType, double seconds)
    {
        var notifications = new Notifications
        {
            SessionStarted = new NotificationConfiguration { RecentEventThreshold = 11 },
            PlaybackStarted = new NotificationConfiguration { RecentEventThreshold = 12 },
            TaskFailed = new NotificationConfiguration { RecentEventThreshold = 13 },
            PluginChanged = new NotificationConfiguration { RecentEventThreshold = 14 },
            SignInFailed = new NotificationConfiguration { RecentEventThreshold = -15 }
        };
        var waitIn = eventType.GetMethod("WaitIn", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        Assert.Equal(TimeSpan.FromSeconds(seconds), (TimeSpan)waitIn.Invoke(null, [notifications])!);
    }

    /// <summary>
    /// A wait as long as the server can count does not stop every other event's cleanup.
    /// </summary>
    /// <remarks>
    /// Its wait plus the cleanup's margin overflowed, and the cleanup that any of the five
    /// events runs first threw, so none of them notified until the server restarted.
    /// </remarks>
    [Fact]
    public void ACleanupCopesWithTheLongestWait()
    {
        var key = $"test-longest-{Guid.NewGuid()}";
        BaseEvent.RecentEvents[key] = (DateTime.UtcNow - TimeSpan.FromDays(1), TimeSpan.MaxValue);

        new Probe(TimeSpan.FromSeconds(5)).Cleanup();

        Assert.True(BaseEvent.RecentEvents.ContainsKey(key));
    }

    /// <summary>
    /// What one event still waits for survives another event's cleanup.
    /// </summary>
    /// <remarks>
    /// The cleanup used the wait of the event that ran it, so a refused sign in told to wait
    /// an hour was forgotten by the next session start after five minutes, and notified again.
    /// </remarks>
    [Fact]
    public void ACleanupKeepsWhatAnotherEventStillWaitsFor()
    {
        var key = $"test-long-{Guid.NewGuid()}";
        BaseEvent.RecentEvents[key] = (DateTime.UtcNow - TimeSpan.FromMinutes(7), TimeSpan.FromHours(1));

        new Probe(TimeSpan.FromSeconds(5)).Cleanup();

        Assert.True(new Probe(TimeSpan.FromHours(1)).Seen(key));
    }

    /// <summary>
    /// An entry past its own wait and the cleanup's margin goes.
    /// </summary>
    [Fact]
    public void ACleanupForgetsWhatNobodyWaitsForAnyMore()
    {
        var key = $"test-old-{Guid.NewGuid()}";
        BaseEvent.RecentEvents[key] = (DateTime.UtcNow - TimeSpan.FromMinutes(11), TimeSpan.FromMinutes(5));

        new Probe(TimeSpan.FromSeconds(5)).Cleanup();

        Assert.False(BaseEvent.RecentEvents.ContainsKey(key));
    }

    private sealed class Probe(TimeSpan wait) : BaseEvent(
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
        new LocalizationHelper(null, null),
        null!,
        null!)
    {
        public bool Seen(string key) => HasRecentlyProcessed(key);

        public void Cleanup() => CleanupOldEntries();

        protected override TimeSpan GetRecentEventThreshold() => wait;
    }
}
