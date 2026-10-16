using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events.ItemAdded;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The countdown that gathers the episodes of a season before one notification names them.
/// </summary>
/// <remarks>
/// Its callback runs on a <see cref="System.Threading.Timer"/>, where an exception nobody
/// catches ends the whole server's process: a season whose series no longer resolves threw
/// there. And the list of episodes was read by the callback while the library scan kept
/// adding to it, which throws "Collection was modified" there too.
/// </remarks>
public class EpisodeTimerTests
{
    /// <summary>
    /// A callback that throws is handed to the countdown's failure callback, not to the timer.
    /// </summary>
    [Fact]
    public void ACallbackThatThrowsIsReportedRatherThanThrown()
    {
        var failures = new List<Exception>();
        var timer = new EpisodeTimer([], _ => throw new InvalidOperationException("the series is gone"), failures.Add);

        timer.Fire(null);

        Assert.Equal("the series is gone", Assert.Single(failures).Message);
    }

    /// <summary>
    /// Episodes added while the callback reads them are neither lost nor read half written.
    /// </summary>
    [Fact]
    public async Task EpisodesAddedWhileTheCallbackReadsThemAreKept()
    {
        var timer = new EpisodeTimer([], _ => { }, _ => { });
        var adding = 0;

        var readers = Task.Run(() =>
        {
            while (Volatile.Read(ref adding) < 4)
            {
                _ = timer.Snapshot().Select(episode => episode.Id).ToList();
            }
        });

        Parallel.For(0, 4, _ =>
        {
            for (var i = 0; i < 500; i++)
            {
                timer.Add(new Episode { Id = Guid.NewGuid() });
            }

            Interlocked.Increment(ref adding);
        });
        await readers;

        Assert.Equal(2000, timer.Snapshot().Count);
    }
}
