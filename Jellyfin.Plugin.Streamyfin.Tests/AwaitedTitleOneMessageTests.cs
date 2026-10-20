using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events.ItemAdded;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A person who waited for a title gets one message when it arrives, the awaited title one, and
/// not the new item one too (#289).
/// </summary>
public class AwaitedTitleOneMessageTests
{
    private static User Person(string name) => new(name, "Default", "Default") { Id = Guid.NewGuid() };

    /// <summary>
    /// The new item message leaves out whoever the awaited title message told, and goes to
    /// everyone else it went to before.
    /// </summary>
    [Fact]
    public void TheNewItemMessageLeavesOutWhoTheAwaitedTitleMessageTold()
    {
        var waited = Person("alex");
        var other = Person("dana");

        var goesTo = ItemAddedService.LeaveOut(new HashSet<Guid> { waited.Id }, _ => true);

        Assert.False(goesTo(waited));
        Assert.True(goesTo(other));
    }

    /// <summary>
    /// Who may open the title still decides: leaving people out never adds anybody.
    /// </summary>
    [Fact]
    public void LeavingPeopleOutNeverLetsInWhoCannotOpenTheTitle()
    {
        var goesTo = ItemAddedService.LeaveOut(new HashSet<Guid>(), _ => false);

        Assert.False(goesTo(Person("kid")));
    }

    /// <summary>
    /// A batch of episodes knows when the run its episodes came in began, which says which
    /// awaited title messages were about this arrival.
    /// </summary>
    [Fact]
    public void ABatchKnowsWhenItsArrivalBegan()
    {
        var began = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        var batch = new EpisodeTimer([], _ => { }, _ => { }) { Began = began };

        Assert.Equal(began, batch.Began);
    }

    /// <summary>
    /// A new item message counts the awaited title messages sent a little before its arrival
    /// began, since the two notice the title in no set order, and none from an arrival before
    /// it, which ended a minute before this one began.
    /// </summary>
    [Fact]
    public void AMessageCountsTheTellingsOfItsOwnArrival()
    {
        var began = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        var since = ItemAddedService.ToldSince(began);

        Assert.True(since < began);
        Assert.True(since > began - TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// The wait for a title's id is what the administrator set, ten minutes when they set
    /// nothing, and nothing the server could not count: a value the page refuses, stored by
    /// a build before it did, falls back to the usual wait rather than failing the event.
    /// </summary>
    /// <param name="seconds">What the configuration says.</param>
    /// <param name="expected">The wait, in seconds.</param>
    [Theory]
    [InlineData(null, 600)]
    [InlineData(0d, 0)]
    [InlineData(90d, 90)]
    [InlineData(3600d, 3600)]
    [InlineData(-5d, 600)]
    [InlineData(double.NaN, 600)]
    [InlineData(double.PositiveInfinity, 600)]
    [InlineData(1e12, 3600)]
    public void TheWaitForATitlesIdIsWhatTheAdministratorSet(double? seconds, double expected)
    {
        Assert.Equal(TimeSpan.FromSeconds(expected), ItemAddedService.WaitForTitleId(seconds));
    }
}
