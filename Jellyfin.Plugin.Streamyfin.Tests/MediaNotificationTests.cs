using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The line a media notification ends with, naming the item.
/// </summary>
[Collection(StaticLibrary.Name)]
public class MediaNotificationTests
{
    private static readonly DateTime Refreshed = new(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// An episode with a number but no season is named by its series and number.
    /// </summary>
    /// <remarks>
    /// The helper asked for "SeriesEpisode" while the resource is "Series Episode", and a
    /// missing resource comes back as its key, so this notification read "SeriesEpisode".
    /// </remarks>
    [Fact]
    public void AnEpisodeWithoutASeasonIsNamedBySeriesAndNumber()
    {
        var series = new Series { Id = Guid.NewGuid(), Name = "The Office" };
        var episode = new Episode { Id = Guid.NewGuid(), SeriesId = series.Id, IndexNumber = 5 };

        var notification = WithLibrary(
            [series],
            () => MediaNotificationHelper.CreateMediaNotification(
                new LocalizationHelper(null, null),
                "Episode added",
                [],
                episode,
                CultureInfo.InvariantCulture));

        Assert.NotNull(notification);
        Assert.Equal("The Office, Episode 05", notification.Body);
    }

    /// <summary>
    /// A movie in a folder of its own has that folder's name until Jellyfin finds its metadata:
    /// its message gives the title and the year, without the ids Radarr writes there, and keeps
    /// every word of the title.
    /// </summary>
    /// <remarks>
    /// Seen on the beta with Radarr's folder naming: the TMDB id in the folder name gives the
    /// movie its id the moment it is added, so its message goes before the refresh, and read
    /// "Nosferatu  [tmdbid-653] (1922)". Read like a search for the title, as Jellyfin's
    /// ParseName reads it, the name also lost words: "Step Up 3D" became "Step Up".
    /// </remarks>
    /// <param name="folder">The folder the movie is in, which is its name.</param>
    /// <param name="expected">What the message says.</param>
    [Theory]
    [InlineData("Nosferatu (1922) [tmdbid-653]", "Nosferatu (1922)")]
    [InlineData("Nosferatu (1922) [imdbid-tt0013442]", "Nosferatu (1922)")]
    [InlineData("Nosferatu (1922) {tmdb-653}", "Nosferatu (1922)")]
    [InlineData("Nosferatu [tmdbid=653] (1922)", "Nosferatu (1922)")]
    [InlineData("Step Up 3D (2010)", "Step Up 3D (2010)")]
    [InlineData("The Good German (2006) [tmdbid-182]", "The Good German (2006)")]
    [InlineData("[REC] 2 (2009) [tmdbid-10664]", "[REC] 2 (2009)")]
    [InlineData("(500) Days of Summer (2009) [tmdbid-19913]", "(500) Days of Summer (2009)")]
    [InlineData("Metropolis [1927]", "Metropolis (1927)")]
    [InlineData(
        "LEGO DC Comics Super Heroes - Justice League vs. Bizarro League (2015) [tmdbid-326215]",
        "LEGO DC Comics Super Heroes - Justice League vs. Bizarro League (2015)")]
    [InlineData("Sintel", "Sintel")]
    public void AMovieStillNamedAfterItsFolderIsNamedByItsTitle(string folder, string expected)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = folder, Path = $"/media/movies/{folder}/{folder} - [WEBDL-1080p].mkv" };

        Assert.Equal(expected, Body(movie));
    }

    /// <summary>
    /// The year a folder name gives wins over the one Jellyfin read off the file name, which may
    /// be part of the title.
    /// </summary>
    [Fact]
    public void TheFolderYearWinsOverAYearReadOffTheFile()
    {
        const string folder = "Blade Runner 2049 (2017) [tmdbid-335984]";
        var movie = new Movie { Id = Guid.NewGuid(), Name = folder, ProductionYear = 2049, Path = $"/media/movies/{folder}/Blade Runner 2049.mkv" };

        Assert.Equal("Blade Runner 2049 (2017)", Body(movie));
    }

    /// <summary>
    /// A movie Jellyfin named from its metadata keeps that name, stamped as refreshed or not: a
    /// provider that failed leaves the stamp off, and the name its metadata gave in.
    /// </summary>
    [Fact]
    public void AMovieNamedByItsMetadataKeepsThatName()
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = "Blade Runner 2049",
            ProductionYear = 2017,
            Path = "/media/movies/Blade Runner 2049 (2017) [tmdbid-335984]/Blade Runner 2049.mkv"
        };

        Assert.Equal("Blade Runner 2049 (2017)", Body(movie));
    }

    /// <summary>
    /// A movie loose among others keeps the name Jellyfin read off its file, which is a title
    /// already.
    /// </summary>
    [Fact]
    public void AMovieInAMixedFolderKeepsTheNameJellyfinReadOffItsFile()
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = "Blade Runner 2049",
            ProductionYear = 2017,
            IsInMixedFolder = true,
            Path = "/media/movies/Blade Runner 2049 (2017).mkv"
        };

        Assert.Equal("Blade Runner 2049 (2017)", Body(movie));
    }

    /// <summary>
    /// A name the metadata gave keeps every bracket in it; only a year the message would repeat
    /// is taken off.
    /// </summary>
    /// <param name="name">The movie's name.</param>
    /// <param name="year">Its year.</param>
    /// <param name="expected">What the message says.</param>
    [Theory]
    [InlineData("(500) Days of Summer", 2009, "(500) Days of Summer (2009)")]
    [InlineData("Halloween (2018)", 2018, "Halloween (2018)")]
    public void AMetadataNameKeepsItsBrackets(string name, int year, string expected)
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = name,
            ProductionYear = year,
            DateLastRefreshed = Refreshed,
            Path = "/media/movies/Some Folder (1999)/movie.mkv"
        };

        Assert.Equal(expected, Body(movie));
    }

    /// <summary>
    /// A batch of episodes names its show by its title. Jellyfin 10.11 names a new show after its
    /// folder, the year and the ids Sonarr writes there with it; Jellyfin 12 already takes the
    /// year and what follows off, and a number in the title is no year.
    /// </summary>
    /// <param name="name">The show's name as Jellyfin gave it.</param>
    /// <param name="folder">The show's folder.</param>
    /// <param name="expected">What the message says.</param>
    [Theory]
    [InlineData("Breaking Bad (2008) [tvdbid-81189]", "Breaking Bad (2008) [tvdbid-81189]", "Breaking Bad - Episode 2 added for Season 1")]
    [InlineData("Galactica 1980", "Galactica 1980 (1980) [tvdbid-71173]", "Galactica 1980 - Episode 2 added for Season 1")]
    public void ABatchOfEpisodesNamesItsShowByItsTitle(string name, string folder, string expected)
    {
        Assert.Equal(expected, EpisodeBody(new Series { Id = Guid.NewGuid(), Name = name, Path = $"/media/shows/{folder}" }));
    }

    /// <summary>
    /// A show its metadata named keeps that name whole in a batch of episodes, a year in it
    /// included, even when it is the name of its folder: the batch's message shows no year of its
    /// own.
    /// </summary>
    /// <param name="folder">The show's folder.</param>
    [Theory]
    [InlineData("Doctor Who (2005)")]
    [InlineData("Doctor Who (2005) [tvdbid-78804]")]
    public void ARefreshedShowKeepsItsMetadataNameInABatch(string folder)
    {
        var show = new Series
        {
            Id = Guid.NewGuid(),
            Name = "Doctor Who (2005)",
            ProductionYear = 2005,
            DateLastRefreshed = Refreshed,
            Path = $"/media/shows/{folder}"
        };

        Assert.Equal("Doctor Who (2005) - Episode 2 added for Season 1", EpisodeBody(show));
    }

    // The body of the message about a show's episode 2 of season 1, arrived alone.
    private static string? EpisodeBody(Series show)
    {
        var season = new Season { Id = Guid.NewGuid(), SeriesId = show.Id, IndexNumber = 1 };
        var episode = new Episode { Id = Guid.NewGuid(), SeriesId = show.Id, SeasonId = season.Id, IndexNumber = 2 };

        var message = WithLibrary(
            [show, season, episode],
            () => ItemAddedService.EpisodesMessage(
                new LocalizationHelper(null, null),
                season,
                show,
                single: episode,
                first: episode,
                total: 1,
                new Audience(CultureInfo.InvariantCulture, null),
                poster: null));

        return message.Body;
    }

    // The last line of a movie's message.
    private static string? Body(Movie movie) =>
        WithLibrary(
            [movie],
            () => MediaNotificationHelper.CreateMediaNotification(
                new LocalizationHelper(null, null),
                "Movie added",
                [],
                movie,
                CultureInfo.InvariantCulture))?.Body;

    // Episode.Series and Episode.Season go through the static BaseItem.LibraryManager,
    // which the server sets at startup. Only lookups by id are answered here.
    internal static T WithLibrary<T>(IEnumerable<BaseItem> items, Func<T> act)
    {
        var library = DispatchProxy.Create<ILibraryManager, LibraryStub>();
        foreach (var item in items)
        {
            ((LibraryStub)(object)library).Items[item.Id] = item;
        }

        var previous = BaseItem.LibraryManager;
        BaseItem.LibraryManager = library;
        try
        {
            return act();
        }
        finally
        {
            BaseItem.LibraryManager = previous;
        }
    }

    /// <summary>
    /// An <see cref="ILibraryManager"/> that knows a few items by id and nothing else.
    /// </summary>
    public class LibraryStub : DispatchProxy
    {
        /// <summary>
        /// The items it answers for.
        /// </summary>
        public Dictionary<Guid, BaseItem> Items { get; } = [];

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ILibraryManager.GetItemById) && args is [Guid id, ..])
            {
                return Items.GetValueOrDefault(id);
            }

            var returns = targetMethod?.ReturnType;
            return returns is { IsValueType: true } && returns != typeof(void) ? Activator.CreateInstance(returns) : null;
        }
    }
}
