using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What an awaited title's arrival says, and what counts as one (#225).
/// </summary>
[Collection(StaticLibrary.Name)]
public class AwaitedTitleAnnouncerTests
{
    private static readonly Audience English = new(CultureInfo.InvariantCulture, ServerUrl: null);

    private static AwaitedTitleAnnouncer Announcer() =>
        new(null!, null!, null!, new LocalizationHelper(null, null), NullLogger<AwaitedTitleAnnouncer>.Instance);

    /// <summary>
    /// A movie is named with its year, and opens its own page.
    /// </summary>
    [Fact]
    public void AMovieOpensItsOwnPage()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "The Matrix", ProductionYear = 1999 };

        var message = Announcer().Message(movie, English);

        Assert.Equal("Now on the server", message.Title);
        Assert.Contains("The Matrix", message.Body, StringComparison.Ordinal);
        Assert.Contains("1999", message.Body, StringComparison.Ordinal);
        var data = Assert.IsType<Dictionary<string, object?>>(message.Data);
        Assert.Equal(movie.Id.ToString("N"), data["id"]);
        Assert.Equal("Movie", data["type"]);
    }

    /// <summary>
    /// A show opens the show's page, the way a batch of episodes does, which the app already
    /// knows how to open.
    /// </summary>
    [Fact]
    public void AShowOpensTheShowsPage()
    {
        var show = new Series { Id = Guid.NewGuid(), Name = "Game of Thrones" };

        var message = Announcer().Message(show, English);

        var data = Assert.IsType<Dictionary<string, object?>>(message.Data);
        Assert.Equal(show.Id.ToString("N"), data["seriesId"]);
        Assert.Equal("Episode", data["type"]);
        Assert.False(data.ContainsKey("id"));
    }

    /// <summary>
    /// A movie is a title, and so is the show of an episode; a virtual item or a season is not.
    /// </summary>
    [Fact]
    public void WhatCountsAsATitle()
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        var show = new Series { Id = Guid.NewGuid(), Name = "Game of Thrones" };
        var episode = new Episode { Id = Guid.NewGuid(), SeriesId = show.Id };

        Assert.Same(movie, AwaitedTitleAnnouncer.TitleOf(movie));
        Assert.Null(AwaitedTitleAnnouncer.TitleOf(new Movie { Id = Guid.NewGuid(), IsVirtualItem = true }));
        Assert.Null(AwaitedTitleAnnouncer.TitleOf(new Season { Id = Guid.NewGuid() }));
        Assert.Same(show, MediaNotificationTests.WithLibrary([show], () => AwaitedTitleAnnouncer.TitleOf(episode)));
    }
}
