using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What the admin page is handed about the events, and what the server refuses to store
/// about them.
/// </summary>
public class NotificationLevelApiTests
{
    /// <summary>
    /// A level goes to the database as JSON and comes back the same, and a level that says
    /// nothing is stored as nothing rather than as a row of nulls.
    /// </summary>
    [Fact]
    public void ALevelIsWrittenTheWayItIsRead()
    {
        var said = new Dictionary<string, NotificationTargeting>
        {
            ["taskFailed"] = new() { Enabled = true },
            ["sessionStarted"] = new() { Enabled = false, RecentEventThreshold = 90 }
        };

        var written = NotificationTargeting.Write(said);
        var read = NotificationTargeting.Read(written);

        Assert.NotNull(read);
        Assert.True(read["taskFailed"].Enabled);
        Assert.Null(read["taskFailed"].RecentEventThreshold);
        Assert.False(read["sessionStarted"].Enabled);
        Assert.Equal(90, read["sessionStarted"].RecentEventThreshold);

        Assert.Equal("{}", NotificationTargeting.Write(null));
        Assert.Equal("{}", NotificationTargeting.Write(new Dictionary<string, NotificationTargeting>()));
    }

    /// <summary>
    /// The events the page offers come from the same place the form comes from, so a new
    /// event appears in both at once.
    /// </summary>
    [Fact]
    public void TheEventsAreDescribedForThePage()
    {
        var events = NotificationsForm.Events();

        Assert.Equal(
            ["sessionStarted", "playbackStarted", "userLockedOut", "itemAdded", "taskFailed", "pluginChanged", "signInFailed"],
            events.Select(one => one.Key));

        Assert.Equal("Session Started", events.First(one => one.Key == "sessionStarted").Title);
    }

    /// <summary>
    /// An event that names somebody else is marked as such, which is what the page warns
    /// about before it hands one to an account that does not administer the server.
    /// </summary>
    [Fact]
    public void TheEventsThatNameSomebodyElseAreMarked()
    {
        var named = NotificationsForm.Events()
            .Where(one => one.AboutSomebodyElse)
            .Select(one => one.Key)
            .ToList();

        Assert.Equal(["sessionStarted", "playbackStarted", "userLockedOut", "signInFailed"], named);
    }

    /// <summary>
    /// A level naming an event this server does not have is refused, rather than stored
    /// and silently ignored on every send.
    /// </summary>
    [Fact]
    public void AnEventThatDoesNotExistIsRefused()
    {
        var problem = NotificationsValidation.CheckTargeting(new Dictionary<string, NotificationTargeting>
        {
            ["taskFaild"] = new() { Enabled = true }
        });

        Assert.NotNull(problem);
        Assert.Contains("taskFaild", problem, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// A wait is a number of seconds here too, and the page is not the only thing that
    /// says so.
    /// </summary>
    [Fact]
    public void AWaitStartsAtZeroHereToo()
    {
        Assert.NotNull(NotificationsValidation.CheckTargeting(new Dictionary<string, NotificationTargeting>
        {
            ["taskFailed"] = new() { RecentEventThreshold = -1 }
        }));

        Assert.Null(NotificationsValidation.CheckTargeting(new Dictionary<string, NotificationTargeting>
        {
            ["taskFailed"] = new() { Enabled = true, RecentEventThreshold = 0 }
        }));

        Assert.Null(NotificationsValidation.CheckTargeting(null));
    }
}
