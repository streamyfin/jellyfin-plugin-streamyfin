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
    /// A batch of episodes knows when its first episode came, which says which awaited title
    /// messages were about this arrival.
    /// </summary>
    [Fact]
    public void ABatchKnowsWhenItsFirstEpisodeCame()
    {
        var before = DateTime.UtcNow;

        var batch = new EpisodeTimer([], _ => { }, _ => { });

        Assert.InRange(batch.Opened, before, DateTime.UtcNow);
    }

    /// <summary>
    /// A batch counts the awaited title messages sent a little before its first episode, since
    /// the two notice that episode in no set order, and none from a batch before it, which closed
    /// a minute before this one opened.
    /// </summary>
    [Fact]
    public void ABatchCountsTheMessagesOfItsOwnArrival()
    {
        var opened = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        var since = ItemAddedService.ToldSinceBatch(opened);

        Assert.True(since < opened);
        Assert.True(since > opened - TimeSpan.FromMinutes(1));
    }
}
