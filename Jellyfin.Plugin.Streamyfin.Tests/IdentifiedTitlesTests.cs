using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events.ItemAdded;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A new item message held until Jellyfin knows the title's TMDB or TVDB id, ten minutes at
/// most, so it can leave out the people the awaited title message tells (#289).
/// </summary>
public class IdentifiedTitlesTests
{
    private static Movie Movie(string? tmdb = null)
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        if (tmdb is not null)
        {
            movie.ProviderIds[nameof(MetadataProvider.Tmdb)] = tmdb;
        }

        return movie;
    }

    /// <summary>
    /// A title Jellyfin already knows by its id is sent at once.
    /// </summary>
    [Fact]
    public void ATitleWithItsIdIsSentAtOnce()
    {
        var sent = 0;
        using var held = new IdentifiedTitles(TimeSpan.FromMinutes(10), _ => { });

        held.Send(Movie("603"), () => sent++);

        Assert.Equal(1, sent);
    }

    /// <summary>
    /// A title without its id waits for the update that brings it, and is sent once.
    /// </summary>
    [Fact]
    public void ATitleWithoutItsIdWaitsForTheUpdateThatBringsIt()
    {
        var sent = 0;
        using var held = new IdentifiedTitles(TimeSpan.FromMinutes(10), _ => { });
        var movie = Movie();

        held.Send(movie, () => sent++);
        held.Updated(movie);
        Assert.Equal(0, sent);

        movie.ProviderIds[nameof(MetadataProvider.Tmdb)] = "603";
        held.Updated(movie);
        held.Updated(movie);

        Assert.Equal(1, sent);
    }

    /// <summary>
    /// An update of another item sends nothing.
    /// </summary>
    [Fact]
    public void AnUpdateOfAnotherItemSendsNothing()
    {
        var sent = 0;
        using var held = new IdentifiedTitles(TimeSpan.FromMinutes(10), _ => { });

        held.Send(Movie(), () => sent++);
        held.Updated(Movie("603"));

        Assert.Equal(0, sent);
    }

    /// <summary>
    /// Two messages held for the same title both go when its id comes.
    /// </summary>
    [Fact]
    public void TwoMessagesHeldForOneTitleBothGo()
    {
        var sent = new List<string>();
        using var held = new IdentifiedTitles(TimeSpan.FromMinutes(10), _ => { });
        var movie = Movie();

        held.Send(movie, () => sent.Add("first"));
        held.Send(movie, () => sent.Add("second"));
        movie.ProviderIds[nameof(MetadataProvider.Tmdb)] = "603";
        held.Updated(movie);

        Assert.Equal(["first", "second"], sent);
    }

    /// <summary>
    /// A title that never gets an id is sent once the longest wait is over.
    /// </summary>
    [Fact]
    public void ATitleThatNeverGetsAnIdIsSentAfterTheLongestWait()
    {
        using var done = new ManualResetEventSlim();
        using var held = new IdentifiedTitles(TimeSpan.FromMilliseconds(50), _ => { });

        held.Send(Movie(), done.Set);

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// A title sent when its id came is not sent again when the longest wait ends.
    /// </summary>
    [Fact]
    public void ATitleSentWhenItsIdCameIsNotSentAgainLater()
    {
        var sent = 0;
        using var held = new IdentifiedTitles(TimeSpan.FromMilliseconds(100), _ => { });
        var movie = Movie();

        held.Send(movie, () => Interlocked.Increment(ref sent));
        movie.ProviderIds[nameof(MetadataProvider.Tmdb)] = "603";
        held.Updated(movie);
        Thread.Sleep(400);

        Assert.Equal(1, Volatile.Read(ref sent));
    }

    /// <summary>
    /// A message that fails is reported, and the next one still goes.
    /// </summary>
    [Fact]
    public void AMessageThatFailsIsReportedAndStopsNothing()
    {
        var failures = new List<Exception>();
        var sent = 0;
        using var held = new IdentifiedTitles(TimeSpan.FromMinutes(10), failures.Add);

        held.Send(Movie("603"), () => throw new InvalidOperationException("the movie is gone"));
        held.Send(Movie("604"), () => sent++);

        Assert.Single(failures);
        Assert.Equal(1, sent);
    }

    /// <summary>
    /// Clearing drops what was held: nothing is sent for it afterwards.
    /// </summary>
    [Fact]
    public void ClearingDropsWhatWasHeld()
    {
        var sent = 0;
        using var held = new IdentifiedTitles(TimeSpan.FromMilliseconds(50), _ => { });

        held.Send(Movie(), () => Interlocked.Increment(ref sent));
        held.Clear();
        Thread.Sleep(300);

        Assert.Equal(0, Volatile.Read(ref sent));
    }
}
