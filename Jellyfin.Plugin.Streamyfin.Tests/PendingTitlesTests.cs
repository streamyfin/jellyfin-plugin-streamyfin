using System;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The titles added before their ids, kept until their metadata brings them (#225).
/// </summary>
public class PendingTitlesTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A title added without ids is waited for.
    /// </summary>
    [Fact]
    public void ATitleAddedWithoutIdsIsWaitedFor()
    {
        var pending = new PendingTitles(TimeSpan.FromHours(24));
        var movie = Guid.NewGuid();

        pending.Add(movie, Now);

        Assert.True(pending.Has(movie, Now.AddHours(1)));
    }

    /// <summary>
    /// After a day it is forgotten, and a later update is not taken for an arrival.
    /// </summary>
    [Fact]
    public void ItIsForgottenAfterADay()
    {
        var pending = new PendingTitles(TimeSpan.FromHours(24));
        var movie = Guid.NewGuid();

        pending.Add(movie, Now);

        Assert.False(pending.Has(movie, Now.AddHours(25)));
    }

    /// <summary>
    /// Adding it again keeps the first day, so a show updated every hour is not kept forever.
    /// </summary>
    [Fact]
    public void AddingItAgainKeepsTheFirstDay()
    {
        var pending = new PendingTitles(TimeSpan.FromHours(24));
        var show = Guid.NewGuid();

        pending.Add(show, Now);
        pending.Add(show, Now.AddHours(20));

        Assert.False(pending.Has(show, Now.AddHours(25)));
    }

    /// <summary>
    /// Taking it ends the wait.
    /// </summary>
    [Fact]
    public void TakingItEndsTheWait()
    {
        var pending = new PendingTitles(TimeSpan.FromHours(24));
        var movie = Guid.NewGuid();

        pending.Add(movie, Now);

        Assert.True(pending.Take(movie, Now));
        Assert.False(pending.Has(movie, Now));
        Assert.False(pending.Take(movie, Now));
    }

    /// <summary>
    /// A library scan asks about every item it touches, so the list is swept at most once a
    /// minute rather than on every question, and an entry past its day is told apart by its
    /// own time meanwhile.
    /// </summary>
    [Fact]
    public void AScanDoesNotSweepTheListOnEveryQuestion()
    {
        var pending = new PendingTitles(TimeSpan.FromHours(24));
        var old = Guid.NewGuid();

        pending.Add(old, Now);
        var before = pending.Sweeps;
        for (var second = 0; second < 50; second++)
        {
            pending.Add(Guid.NewGuid(), Now.AddHours(25).AddSeconds(second));
            pending.Has(Guid.NewGuid(), Now.AddHours(25).AddSeconds(second));
        }

        Assert.Equal(before + 1, pending.Sweeps);
        Assert.False(pending.Has(old, Now.AddHours(25)));
    }
}
