using System;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Which update settles a title added before it had its ids (#225).
/// </summary>
[Collection(StaticLibrary.Name)]
public class AwaitedTitlesServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 2, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A show waits under its own id, from its first episode. Its ids come with its own
    /// update, and an episode's update stands for the show too, as when it was added.
    /// </summary>
    [Fact]
    public void AShowIsSettledByItsOwnUpdateOrAnEpisodes()
    {
        var show = new Series { Id = Guid.NewGuid() };
        var episode = new Episode { Id = Guid.NewGuid(), SeriesId = show.Id };
        var pending = new PendingTitles(TimeSpan.FromHours(24));
        pending.Add(show.Id, Now);

        Assert.Same(show, AwaitedTitlesService.SettledBy(pending, show, Now));
        Assert.Same(show, MediaNotificationTests.WithLibrary([show], () => AwaitedTitlesService.SettledBy(pending, episode, Now)));
    }

    /// <summary>
    /// A movie is settled by its own update.
    /// </summary>
    [Fact]
    public void AMovieIsSettledByItsOwnUpdate()
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        var pending = new PendingTitles(TimeSpan.FromHours(24));
        pending.Add(movie.Id, Now);

        Assert.Same(movie, AwaitedTitlesService.SettledBy(pending, movie, Now));
    }

    /// <summary>
    /// An update of a title nobody waits for settles nothing.
    /// </summary>
    [Fact]
    public void AnUpdateNobodyWaitsForSettlesNothing()
    {
        var pending = new PendingTitles(TimeSpan.FromHours(24));
        pending.Add(Guid.NewGuid(), Now);

        Assert.Null(AwaitedTitlesService.SettledBy(pending, new Movie { Id = Guid.NewGuid() }, Now));
        Assert.Null(AwaitedTitlesService.SettledBy(pending, new Season { Id = Guid.NewGuid() }, Now));
    }
}
