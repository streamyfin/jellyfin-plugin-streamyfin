using System;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What a person keeps of their notifications, as stored and as read back (P4.5).
/// </summary>
public class NotificationPreferencesTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// An event the person never mentioned is kept.
    /// </summary>
    [Fact]
    public void AnEventNobodyMentionedIsKept()
    {
        Assert.True(new NotificationPreferences().Keeps("itemAdded"));
    }

    /// <summary>
    /// An event turned off is not kept, and the others still are.
    /// </summary>
    [Fact]
    public void AnEventTurnedOffIsNotKept()
    {
        var mine = new NotificationPreferences();
        mine.Events["itemAdded"] = false;

        Assert.False(mine.Keeps("itemAdded"));
        Assert.True(mine.Keeps("taskFailed"));
    }

    /// <summary>
    /// A pause whose end has passed is no pause.
    /// </summary>
    [Fact]
    public void APauseThatEndedIsNoPause()
    {
        var mine = new NotificationPreferences { Pause = new NotificationPause { Until = Now.AddMinutes(-1) } };

        Assert.False(mine.IsPaused(Now));
    }

    /// <summary>
    /// A pause holds until its end.
    /// </summary>
    [Fact]
    public void APauseLastsUntilItsEnd()
    {
        var mine = new NotificationPreferences { Pause = new NotificationPause { Until = Now.AddHours(8) } };

        Assert.True(mine.IsPaused(Now));
    }

    /// <summary>
    /// A pause without an end lasts until it is lifted.
    /// </summary>
    [Fact]
    public void APauseWithoutAnEndLastsUntilItIsLifted()
    {
        var mine = new NotificationPreferences { Pause = new NotificationPause() };

        Assert.True(mine.IsPaused(Now.AddYears(1)));
    }

    /// <summary>
    /// A row edited by hand into nonsense means everything is kept.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("[]")]
    public void ARowThatCannotBeReadSaysNothing(string? stored)
    {
        Assert.Null(NotificationPreferences.Read(stored));
    }

    /// <summary>
    /// Lists written as null in a row read as empty, and the follow choice as its defaults.
    /// </summary>
    [Fact]
    public void NullListsInARowReadAsEmpty()
    {
        var mine = NotificationPreferences.Read("""{"events":null,"mutedLibraries":null,"mutedShows":null,"follow":null}""");

        Assert.NotNull(mine);
        Assert.Empty(mine!.Events);
        Assert.Empty(mine.MutedLibraries);
        Assert.Empty(mine.MutedShows);
        Assert.True(mine.Follow.Favorites);
        Assert.True(mine.Follow.Started);
    }

    /// <summary>
    /// What is written for storage reads back the same.
    /// </summary>
    [Fact]
    public void WhatIsWrittenReadsBack()
    {
        var library = Guid.NewGuid();
        var show = Guid.NewGuid();
        var mine = new NotificationPreferences
        {
            Pause = new NotificationPause(),
            MutedLibraries = [library],
            MutedShows = [show],
            Follow = new FollowChoice { Favorites = true, Started = false }
        };
        mine.Events["seerrRequests"] = false;

        var read = NotificationPreferences.Read(NotificationPreferences.Write(mine));

        Assert.NotNull(read);
        Assert.True(read!.IsPaused(Now));
        Assert.Equal([library], read.MutedLibraries);
        Assert.Equal([show], read.MutedShows);
        Assert.False(read.Follow.Started);
        Assert.False(read.Keeps("seerrRequests"));
    }
}
