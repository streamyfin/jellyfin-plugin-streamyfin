using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Who an event reaches, once a group or a user says something about it.
/// </summary>
/// <remarks>
/// The server says whether an event is on at all, and who it is for by default: the
/// administrators for the ones about the server itself, everybody for a new item. A group
/// or a user can say otherwise, and the most specific level that says anything wins, which
/// is how every other setting in this plugin resolves.
/// </remarks>
public class NotificationTargetingTests
{
    private const string Event = "taskFailed";

    /// <summary>
    /// What the levels say about one user, read from what the database holds for them.
    /// </summary>
    [Fact]
    public void AUserIsAskedAboutThroughTheirOwnLevels()
    {
        var alice = System.Guid.NewGuid();
        var bob = System.Guid.NewGuid();

        var targets = new NotificationTargets(new Dictionary<string, List<string>>
        {
            [alice.ToString()] = [],
            [bob.ToString()] = []
        }.ToDictionary(pair => System.Guid.Parse(pair.Key), pair => pair.Value));

        Assert.True(targets.Reaches(Event, alice, serverEnabled: true, inDefaultAudience: true));
        Assert.False(targets.Reaches(Event, alice, serverEnabled: true, inDefaultAudience: false));

        var given = new NotificationTargets(new Dictionary<Guid, List<string>>
        {
            [bob] = ["""{"taskFailed": {"enabled": true}}"""]
        });

        Assert.True(given.Reaches(Event, bob, serverEnabled: false, inDefaultAudience: false));
        Assert.False(given.Reaches(Event, alice, serverEnabled: false, inDefaultAudience: false));
    }

    private static Dictionary<string, NotificationTargeting>? Level(bool? enabled) =>
        enabled is null ? null : new Dictionary<string, NotificationTargeting> { [Event] = new() { Enabled = enabled } };

    /// <summary>
    /// With nothing said about it anywhere, the event follows the server: on for the people
    /// it is for, off for everybody else.
    /// </summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void WithNothingSaidTheServerDecides(bool serverEnabled, bool theirs, bool expected)
    {
        Assert.Equal(expected, NotificationTargeting.Reaches(Event, serverEnabled, theirs, []));
    }

    /// <summary>
    /// A group turns an event on for people the server left out, and off for people it
    /// included.
    /// </summary>
    [Fact]
    public void AGroupSaysOtherwise()
    {
        Assert.True(NotificationTargeting.Reaches(Event, serverEnabled: true, inDefaultAudience: false, [Level(true)]));
        Assert.False(NotificationTargeting.Reaches(Event, serverEnabled: true, inDefaultAudience: true, [Level(false)]));
    }

    /// <summary>
    /// The user's own level is the last word, over any group they are in.
    /// </summary>
    [Fact]
    public void TheUserIsTheLastWord()
    {
        Assert.False(NotificationTargeting.Reaches(Event, serverEnabled: true, inDefaultAudience: true, [Level(true), Level(false)]));
        Assert.True(NotificationTargeting.Reaches(Event, serverEnabled: false, inDefaultAudience: false, [Level(false), Level(true)]));
    }

    /// <summary>
    /// A level that says nothing about this event leaves the one before it standing.
    /// </summary>
    [Fact]
    public void ALevelThatSaysNothingChangesNothing()
    {
        Assert.True(NotificationTargeting.Reaches(Event, serverEnabled: true, inDefaultAudience: false, [Level(true), null, Level(null)]));

        var elsewhere = new Dictionary<string, NotificationTargeting> { ["sessionStarted"] = new() { Enabled = false } };

        Assert.True(NotificationTargeting.Reaches(Event, serverEnabled: true, inDefaultAudience: true, [elsewhere]));
    }

    /// <summary>
    /// An event switched off on the server still reaches whoever was given it, since that
    /// is what giving it to them means.
    /// </summary>
    [Fact]
    public void AnEventOffOnTheServerStillReachesWhoWasGivenIt()
    {
        Assert.True(NotificationTargeting.Reaches(Event, serverEnabled: false, inDefaultAudience: true, [Level(true)]));
    }

    /// <summary>
    /// A level is stored as JSON, and one that cannot be read says nothing rather than
    /// taking the whole send down.
    /// </summary>
    [Fact]
    public void ALevelIsReadFromJsonAndABadOneSaysNothing()
    {
        var said = NotificationTargeting.Read("""{"taskFailed": {"enabled": true, "recentEventThreshold": 30}}""");

        Assert.NotNull(said);
        Assert.True(said[Event].Enabled);
        Assert.Equal(30, said[Event].RecentEventThreshold);

        Assert.Null(NotificationTargeting.Read(null));
        Assert.Null(NotificationTargeting.Read(""));
        Assert.Null(NotificationTargeting.Read("{}"));
        Assert.Null(NotificationTargeting.Read("not json"));
        Assert.Null(NotificationTargeting.Read("""{"taskFailed": "yes"}"""));
    }

    /// <summary>
    /// The wait between two of the same event is resolved the same way, and the server's
    /// own is the fallback.
    /// </summary>
    [Fact]
    public void TheWaitResolvesTheSameWay()
    {
        var levels = new[]
        {
            new Dictionary<string, NotificationTargeting> { [Event] = new() { RecentEventThreshold = 60 } },
            new Dictionary<string, NotificationTargeting> { [Event] = new() { Enabled = true } }
        };

        Assert.Equal(60, NotificationTargeting.WaitOf(Event, serverWait: 5, levels));
        Assert.Equal(5, NotificationTargeting.WaitOf(Event, serverWait: 5, []));
        Assert.Null(NotificationTargeting.WaitOf(Event, serverWait: null, []));
    }
}
