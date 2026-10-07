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
    /// An update that leaves things out stores the defaults for them.
    /// </summary>
    [Fact]
    public void AnUpdateThatSaysLittleKeepsTheDefaults()
    {
        var applied = MyNotifications.Apply(new MyNotificationsUpdate());

        Assert.Null(MyNotifications.Problem(new MyNotificationsUpdate()));
        Assert.Null(applied.Pause);
        Assert.Empty(applied.Events);
        Assert.True(applied.Follow.Favorites);
        Assert.True(applied.Follow.Started);
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
    /// An update with more shows or libraries than the most is refused: every send reads
    /// everyone's choices, so no list in them grows without end.
    /// </summary>
    [Fact]
    public void AnUpdateWithMoreThanTheMostIsRefused()
    {
        var shows = new MyNotificationsUpdate { MutedShows = [.. Enumerable.Range(0, MyNotifications.MostMutedShows + 1).Select(_ => Guid.NewGuid())] };
        var libraries = new MyNotificationsUpdate { MutedLibraries = [.. Enumerable.Range(0, MyNotifications.MostMutedLibraries + 1).Select(_ => Guid.NewGuid())] };

        Assert.NotNull(MyNotifications.Problem(shows));
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
