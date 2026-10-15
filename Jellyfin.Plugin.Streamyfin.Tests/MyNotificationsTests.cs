using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Api;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The notification choices a person reads and writes from the app (P4.5).
/// </summary>
public class MyNotificationsTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Movies = Guid.NewGuid();
    private static readonly Guid Music = Guid.NewGuid();

    /// <summary>
    /// Only the events that can reach the person are described, all kept when they chose
    /// nothing.
    /// </summary>
    [Fact]
    public void OnlyWhatReachesThePersonIsDescribed()
    {
        var described = MyNotifications.Describe(
            null,
            ["itemAdded", "userLockedOut"],
            [(Movies, "Movies"), (Music, "Music videos")],
            [],
            _ => null);

        Assert.Equal(["itemAdded", "userLockedOut"], described.Events.Select(e => e.Key));
        Assert.All(described.Events, e => Assert.True(e.Enabled));
        Assert.Equal("new-content", described.Events[0].Family);
        Assert.All(described.Libraries, library => Assert.True(library.Enabled));
        Assert.Null(described.Pause);
    }

    /// <summary>
    /// What the person turned off is described off, and a muted show by its name.
    /// </summary>
    [Fact]
    public void WhatThePersonTurnedOffIsDescribedOff()
    {
        var mine = new NotificationPreferences { MutedLibraries = [Music] };
        mine.Events["itemAdded"] = false;
        var show = Guid.NewGuid();
        mine.MutedShows.Add(show);

        var described = MyNotifications.Describe(mine, ["itemAdded"], [(Movies, "Movies"), (Music, "Music videos")], [show], id => id == show ? "The Bear" : null);

        Assert.False(described.Events.Single().Enabled);
        Assert.False(described.Libraries.Single(l => l.Id == Music).Enabled);
        Assert.Equal("The Bear", described.MutedShows.Single().Name);
    }

    /// <summary>
    /// An update naming an event that does not exist is refused.
    /// </summary>
    [Fact]
    public void AnEventNobodyKnowsIsRefused()
    {
        var update = new MyNotificationsUpdate { Events = new() { ["somethingElse"] = false } };

        Assert.NotNull(MyNotifications.Problem(update));
    }

    /// <summary>
    /// An update that says nothing about a part leaves it as it is: a choice stands until the
    /// person changes it.
    /// </summary>
    [Fact]
    public void AnUpdateThatSaysNothingChangesNothing()
    {
        var muted = Guid.NewGuid();
        var mine = new NotificationPreferences
        {
            MutedLibraries = [muted],
            Follow = new FollowChoice { Favorites = false, Started = false }
        };
        mine.Events["itemAdded"] = false;

        Assert.Null(MyNotifications.Apply(new MyNotificationsUpdate(), mine, [Movies], [Movies, Music, muted]));

        Assert.Null(MyNotifications.Problem(new MyNotificationsUpdate()));
        Assert.False(mine.Keeps("itemAdded"));
        Assert.Equal([muted], mine.MutedLibraries);
        Assert.False(mine.Follow.Favorites);
        Assert.False(mine.Follow.Started);
    }

    /// <summary>
    /// The app only sends the events that reach the person now. One that stops reaching them for
    /// a while, because an administrator turned it off for the server, keeps their choice, and does
    /// not come back on when it reaches them again (#272).
    /// </summary>
    [Fact]
    public void AnEventTheUpdateLeavesOutKeepsItsChoice()
    {
        var mine = new NotificationPreferences();
        mine.Events["playbackStarted"] = false;

        MyNotifications.Apply(new MyNotificationsUpdate { Events = new() { ["itemAdded"] = false } }, mine, [], [Movies, Music]);

        Assert.False(mine.Keeps("playbackStarted"));
        Assert.False(mine.Keeps("itemAdded"));
    }

    /// <summary>
    /// The update speaks for the libraries the person is offered: one they turned back on there
    /// is on, and one they are not offered now stays off (#272).
    /// </summary>
    [Fact]
    public void TheUpdateSpeaksForTheOfferedLibrariesOnly()
    {
        var mine = new NotificationPreferences { MutedLibraries = [Movies, Music] };

        MyNotifications.Apply(new MyNotificationsUpdate { MutedLibraries = [] }, mine, [Movies], [Movies, Music]);

        Assert.Equal([Music], mine.MutedLibraries);
    }

    /// <summary>
    /// The update has no say over a library the person is not offered, to turn it off any more
    /// than back on: an id sent for one is left out, whatever the client puts there.
    /// </summary>
    [Fact]
    public void ALibraryNotOfferedIsNotTurnedOffByTheUpdate()
    {
        var mine = new NotificationPreferences();

        MyNotifications.Apply(new MyNotificationsUpdate { MutedLibraries = [Movies, Music] }, mine, [Movies], [Movies, Music]);

        Assert.Equal([Movies], mine.MutedLibraries);
    }

    /// <summary>
    /// A library the server no longer has is forgotten: no route could turn it back on, it would
    /// count under the most a person can turn off, and a library made again under its name gets
    /// the same id.
    /// </summary>
    [Fact]
    public void ALibraryTheServerNoLongerHasIsForgotten()
    {
        var gone = Guid.NewGuid();
        var mine = new NotificationPreferences { MutedLibraries = [gone, Music] };

        MyNotifications.Apply(new MyNotificationsUpdate { MutedLibraries = [] }, mine, [Movies], [Movies, Music]);

        Assert.Equal([Music], mine.MutedLibraries);
    }

    /// <summary>
    /// Libraries the server no longer has do not count under the most either.
    /// </summary>
    [Fact]
    public void LibrariesTheServerNoLongerHasDoNotCountUnderTheMost()
    {
        var gone = Enumerable.Range(0, MyNotifications.MostMutedLibraries).Select(_ => Guid.NewGuid()).ToList();
        var mine = new NotificationPreferences { MutedLibraries = [.. gone] };

        var problem = MyNotifications.Apply(new MyNotificationsUpdate { MutedLibraries = [Movies] }, mine, [Movies], [Movies]);

        Assert.Null(problem);
        Assert.Equal([Movies], mine.MutedLibraries);
    }

    /// <summary>
    /// A library sent twice, or already off, is kept off once.
    /// </summary>
    [Fact]
    public void ALibraryIsTurnedOffOnce()
    {
        var mine = new NotificationPreferences { MutedLibraries = [Movies] };

        MyNotifications.Apply(new MyNotificationsUpdate { MutedLibraries = [Movies, Movies] }, mine, [Movies], [Movies, Music]);

        Assert.Equal([Movies], mine.MutedLibraries);
    }

    /// <summary>
    /// The libraries kept off, the ones the update speaks for and the others together, stay under
    /// the most a person can turn off, and an update that would pass it changes nothing.
    /// </summary>
    [Fact]
    public void TheLibrariesKeptOffStayUnderTheMost()
    {
        var notOffered = Enumerable.Range(0, MyNotifications.MostMutedLibraries).Select(_ => Guid.NewGuid()).ToList();
        var mine = new NotificationPreferences { MutedLibraries = [.. notOffered] };

        var problem = MyNotifications.Apply(new MyNotificationsUpdate { MutedLibraries = [Movies] }, mine, [Movies], [Movies, .. notOffered]);

        Assert.NotNull(problem);
        Assert.Equal(notOffered, mine.MutedLibraries);
    }

    /// <summary>
    /// An update leaves the pause and the muted shows as they are: they have routes of their
    /// own, and a screen opened before a notification's button was pressed would otherwise
    /// take back what the button did with its next switch.
    /// </summary>
    [Fact]
    public void AnUpdateLeavesThePauseAndTheMutedShowsAlone()
    {
        var show = Guid.NewGuid();
        var mine = new NotificationPreferences
        {
            Pause = new NotificationPause { Until = DateTime.UtcNow.AddHours(8) },
            MutedShows = [show]
        };

        MyNotifications.Apply(new MyNotificationsUpdate { Events = new() { ["itemAdded"] = false } }, mine, [], [Movies, Music]);

        Assert.NotNull(mine.Pause);
        Assert.Equal([show], mine.MutedShows);
        Assert.False(mine.Keeps("itemAdded"));
    }

    /// <summary>
    /// An update from an app that still sends the pause and the muted shows is read, and they
    /// are left out.
    /// </summary>
    [Fact]
    public void AnUpdateFromAnOlderAppIsStillRead()
    {
        var update = System.Text.Json.JsonSerializer.Deserialize<MyNotificationsUpdate>(
            """{"pause":null,"events":{"itemAdded":false},"mutedLibraries":[],"follow":{"favorites":true,"started":false},"mutedShows":[]}""");

        Assert.NotNull(update);
        Assert.False(update!.Events!["itemAdded"]);
        Assert.False(update.Follow!.Started);
    }

    /// <summary>
    /// A pause asked for a number of hours ends that many hours later.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(24)]
    public void APauseEndsAfterTheHoursAsked(int hours)
    {
        Assert.Equal(Now.AddHours(hours), MyNotifications.PauseFor(hours, Now)!.Until);
    }

    /// <summary>
    /// A pause asked without hours lasts until it is lifted.
    /// </summary>
    [Fact]
    public void APauseWithoutHoursLastsUntilLifted()
    {
        Assert.Null(MyNotifications.PauseFor(null, Now)!.Until);
    }

    /// <summary>
    /// A pause of no time, of negative time or of more than a week is refused.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(169)]
    public void APauseOfNoTimeOrMoreThanAWeekIsRefused(int hours)
    {
        Assert.Null(MyNotifications.PauseFor(hours, Now));
    }

    /// <summary>
    /// A library the server never announces, or one the person cannot open, has nothing for
    /// them to turn off.
    /// </summary>
    [Fact]
    public void OnlyTheLibrariesThatCanAnnounceToThePersonAreListed()
    {
        var hidden = Guid.NewGuid();
        VirtualFolderInfo[] folders =
        [
            new() { ItemId = Movies.ToString("N"), Name = "Movies" },
            new() { ItemId = Music.ToString("N"), Name = "Music videos" },
            new() { ItemId = hidden.ToString("N"), Name = "Private" }
        ];

        var listed = MyNotifications.LibrariesFor(folders, [Movies.ToString("N"), hidden.ToString("N")], id => id != hidden);

        Assert.Equal([(Movies, "Movies")], listed);
    }

    /// <summary>
    /// Every library the person can open is listed when the server announces them all.
    /// </summary>
    [Fact]
    public void EveryOpenLibraryIsListedWhenTheServerAnnouncesThemAll()
    {
        VirtualFolderInfo[] folders =
        [
            new() { ItemId = Movies.ToString("N"), Name = "Movies" },
            new() { ItemId = Music.ToString("N"), Name = "Music videos" }
        ];

        Assert.Equal([Movies, Music], MyNotifications.LibrariesFor(folders, null, _ => true).Select(library => library.Id));
    }

    /// <summary>
    /// An update with more libraries than the most is refused: every send reads everyone's
    /// choices, so no list in them grows without end.
    /// </summary>
    [Fact]
    public void AnUpdateWithMoreThanTheMostIsRefused()
    {
        var libraries = new MyNotificationsUpdate { MutedLibraries = [.. Enumerable.Range(0, MyNotifications.MostMutedLibraries + 1).Select(_ => Guid.NewGuid())] };

        Assert.NotNull(MyNotifications.Problem(libraries));
    }

    /// <summary>
    /// Turning the same show off twice lists it once.
    /// </summary>
    [Fact]
    public void AShowIsTurnedOffOnce()
    {
        var mine = new NotificationPreferences();
        var show = Guid.NewGuid();

        Assert.Null(MyNotifications.Mute(mine, show));
        Assert.Null(MyNotifications.Mute(mine, show));
        Assert.Single(mine.MutedShows);
    }

    /// <summary>
    /// Turning off one show more than the most is refused, and the list stays as it was.
    /// </summary>
    [Fact]
    public void TurningOffOneShowPastTheMostIsRefused()
    {
        var mine = new NotificationPreferences { MutedShows = [.. Enumerable.Range(0, MyNotifications.MostMutedShows).Select(_ => Guid.NewGuid())] };

        Assert.NotNull(MyNotifications.Mute(mine, Guid.NewGuid()));
        Assert.Equal(MyNotifications.MostMutedShows, mine.MutedShows.Count);
    }
}
