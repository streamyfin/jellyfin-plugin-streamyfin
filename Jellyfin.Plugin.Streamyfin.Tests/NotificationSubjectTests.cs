using System;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Which family an event belongs to, and how its messages stack.
/// </summary>
public class NotificationSubjectTests
{
    [Theory]
    [InlineData("itemAdded", "new-content")]
    [InlineData("seerrRequests", "requests")]
    [InlineData("userLockedOut", "account")]
    [InlineData("seerrPending", "server-alerts")]
    [InlineData("taskFailed", "server-alerts")]
    [InlineData("signInFailed", "server-alerts")]
    public void EachEventHasItsFamily(string eventKey, string family)
    {
        Assert.Equal(family, NotificationFamilies.Of(eventKey));
    }

    [Fact]
    public void EpisodesStackByShowAndTheRestByFamily()
    {
        var show = Guid.Parse("a656b907eb3a73532e40e44b968d0225");

        Assert.Equal("series-a656b907eb3a73532e40e44b968d0225", new NotificationSubject("itemAdded", SeriesId: show).ThreadId);
        Assert.Equal("new-content", new NotificationSubject("itemAdded").ThreadId);
        Assert.Equal("server-alerts", new NotificationSubject("taskFailed").ThreadId);
    }

    [Fact]
    public void OnlyAnEpisodeOffersToTurnItsShowOff()
    {
        Assert.Equal("episode", new NotificationSubject("itemAdded", SeriesId: Guid.NewGuid()).CategoryId);
        Assert.Equal("general", new NotificationSubject("itemAdded").CategoryId);
        Assert.DoesNotContain('-', new NotificationSubject("itemAdded").CategoryId);
    }

    [Fact]
    public void EveryEventAPersonCanTurnOffIsListedOnce()
    {
        Assert.Equal(9, NotificationEvents.All.Count);
        Assert.Equal(NotificationEvents.All.Count, new System.Collections.Generic.HashSet<string>(NotificationEvents.All).Count);
    }
}
