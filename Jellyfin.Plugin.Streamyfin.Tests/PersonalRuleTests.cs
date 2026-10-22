using System;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What a person keeps, once their levels sent an event to them (P4.5).
/// </summary>
public class PersonalRuleTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Movies = Guid.NewGuid();
    private static readonly Guid Show = Guid.NewGuid();
    private static readonly Func<Guid, bool> Unfollowed = _ => false;
    private static readonly Func<Guid, bool> Followed = _ => true;

    private static NotificationSubject AMovie => new(NotificationEvents.ItemAdded, Movies);
    private static NotificationSubject AnEpisode => new(NotificationEvents.ItemAdded, Movies, Show);

    /// <summary>
    /// Somebody who chose nothing keeps everything their levels send.
    /// </summary>
    [Fact]
    public void SomebodyWhoChoseNothingKeepsEverything()
    {
        Assert.True(PersonalRule.Keeps(null, AMovie, Now, Unfollowed));
    }

    /// <summary>
    /// A pause stops everything, followed shows and server alerts included.
    /// </summary>
    [Fact]
    public void APauseStopsEverything()
    {
        var mine = new NotificationPreferences { Pause = new NotificationPause() };

        Assert.False(PersonalRule.Keeps(mine, AMovie, Now, Followed));
        Assert.False(PersonalRule.Keeps(mine, AnEpisode, Now, Followed));
        Assert.False(PersonalRule.Keeps(mine, new NotificationSubject("taskFailed"), Now, Unfollowed));
    }

    /// <summary>
    /// A movie in a library the person turned off is not kept.
    /// </summary>
    [Fact]
    public void AMovieInAMutedLibraryIsNotKept()
    {
        var mine = new NotificationPreferences { MutedLibraries = [Movies] };

        Assert.False(PersonalRule.Keeps(mine, AMovie, Now, Unfollowed));
    }

    /// <summary>
    /// New items turned off stop movies and the episodes of shows nobody follows.
    /// </summary>
    [Fact]
    public void NewItemsTurnedOffStopMoviesAndUnfollowedEpisodes()
    {
        var mine = new NotificationPreferences();
        mine.Events[NotificationEvents.ItemAdded] = false;

        Assert.False(PersonalRule.Keeps(mine, AMovie, Now, Unfollowed));
        Assert.False(PersonalRule.Keeps(mine, AnEpisode, Now, Unfollowed));
    }

    /// <summary>
    /// A followed show still notifies with new items and its library turned off.
    /// </summary>
    [Fact]
    public void AFollowedShowStillNotifiesWithNewItemsAndItsLibraryOff()
    {
        var mine = new NotificationPreferences { MutedLibraries = [Movies] };
        mine.Events[NotificationEvents.ItemAdded] = false;

        Assert.True(PersonalRule.Keeps(mine, AnEpisode, Now, Followed));
    }

    /// <summary>
    /// A show turned off stays off, even when it is followed.
    /// </summary>
    [Fact]
    public void AShowTurnedOffStaysOffEvenFollowed()
    {
        var mine = new NotificationPreferences { MutedShows = [Show] };

        Assert.False(PersonalRule.Keeps(mine, AnEpisode, Now, Followed));
    }

    /// <summary>
    /// Another event turned off is not kept, and the rest still are.
    /// </summary>
    [Fact]
    public void AnotherEventTurnedOffIsNotKept()
    {
        var mine = new NotificationPreferences();
        mine.Events["taskFailed"] = false;

        Assert.False(PersonalRule.Keeps(mine, new NotificationSubject("taskFailed"), Now, Unfollowed));
        Assert.True(PersonalRule.Keeps(mine, new NotificationSubject("signInFailed"), Now, Unfollowed));
    }

    /// <summary>
    /// Asking whether somebody follows a show costs library queries, so it is only asked when
    /// the episode would not reach them otherwise.
    /// </summary>
    [Fact]
    public void SomebodyWhoKeepsNewItemsIsNotAskedAboutFollowing()
    {
        var asked = false;

        Assert.True(PersonalRule.Keeps(new NotificationPreferences(), AnEpisode, Now, _ => asked = true));
        Assert.False(asked);
    }

    /// <summary>
    /// A show is followed the way the person said: as a favorite, as started, or both.
    /// </summary>
    [Theory]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, true, true)]
    [InlineData(false, false, true, true, false)]
    public void AShowIsFollowedTheWayThePersonSaid(bool favorites, bool started, bool isFavorite, bool hasStarted, bool expected)
    {
        var choice = new FollowChoice { Favorites = favorites, Started = started };

        Assert.Equal(expected, PersonalRule.Follows(choice, isFavorite, () => hasStarted));
    }

    /// <summary>
    /// Asking whether a show was started costs a library query, so a favorite does not ask.
    /// </summary>
    [Fact]
    public void AFavoriteDoesNotAskWhetherTheShowWasStarted()
    {
        var asked = false;

        Assert.True(PersonalRule.Follows(new FollowChoice(), isFavorite: true, () => asked = true));
        Assert.False(asked);
    }
}
