using System;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The task that tells people about what arrived during their pause (#225).
/// </summary>
public class AwaitedTitlesTaskTests
{
    /// <summary>
    /// It runs every quarter of an hour, under the plugin's own category, so a pause that ends
    /// is followed by its news within that time.
    /// </summary>
    [Fact]
    public void ItRunsEveryQuarterOfAnHour()
    {
        var task = new AwaitedTitlesTask(null!, NullLoggerFactory.Instance);

        Assert.Equal("Streamyfin", task.Category);
        Assert.Equal("Jellyfin.Plugin.Streamyfin.AwaitedTitles", task.Key);
        Assert.Equal(TimeSpan.FromMinutes(15).Ticks, Assert.Single(task.GetDefaultTriggers()).IntervalTicks);
    }
}
