using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A clock a test moves by hand. Its timers fire on the test's own thread, when the clock
/// passes their time, so nothing depends on how loaded the machine running the tests is.
/// </summary>
internal sealed class TestClock : TimeProvider
{
    private readonly List<TestTimer> _timers = [];
    private readonly object _gate = new();
    private DateTimeOffset _now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new TestTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>
    /// Gets how many timers are waiting to fire.
    /// </summary>
    public int Armed
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(timer => timer.Due is not null);
            }
        }
    }

    /// <summary>
    /// Moves the clock on, firing every timer it passes, in the order they are due.
    /// </summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by)
    {
        DateTimeOffset until;
        lock (_gate)
        {
            until = _now + by;
        }

        while (true)
        {
            TestTimer? next;
            lock (_gate)
            {
                next = _timers.Where(timer => timer.Due <= until).MinBy(timer => timer.Due);
                if (next is null)
                {
                    _now = until;
                    return;
                }

                _now = next.Due!.Value;
            }

            next.Fire();
        }
    }

    private sealed class TestTimer(TestClock clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            // Every timer the plugin creates fires once, so a period is not something to keep.
            lock (clock._gate)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
            }

            return true;
        }

        public void Fire()
        {
            lock (clock._gate)
            {
                Due = null;
            }

            callback(state);
        }

        public void Dispose()
        {
            lock (clock._gate)
            {
                Due = null;
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
