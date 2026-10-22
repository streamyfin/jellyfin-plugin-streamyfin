using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
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
    /// A movie's message gives its title and its year, once: without the ids Radarr writes in a
    /// folder name, and with a year the name carries in brackets in the year's place, the one at
    /// the end of the name, a folder's, over one Jellyfin read off the file. Nothing else in the
    /// name changes.
    /// </summary>
    /// <remarks>
    /// Until its metadata names it, Jellyfin names a new movie after its folder. Its message
    /// waits for that name, two minutes at most; these are the names it goes with when Jellyfin
    /// did not name it in time, a title no provider knows or a scan's walk, and names a metadata
    /// gave. Seen on the beta: "Nosferatu  [tmdbid-653] (1922)".
    /// </remarks>
    /// <param name="name">The movie's name.</param>
    /// <param name="year">Its year, as Jellyfin has it.</param>
    /// <param name="expected">What the message says.</param>
    [Theory]
    [InlineData("Nosferatu (1922) [tmdbid-653]", null, "Nosferatu (1922)")]
    [InlineData("Nosferatu (1922) [tmdbid-653]", 1922, "Nosferatu (1922)")]
    [InlineData("Nosferatu (1922) {tmdb-653}", null, "Nosferatu (1922)")]
    [InlineData("Nosferatu [tmdbid=653] (1922)", null, "Nosferatu (1922)")]
    [InlineData("Nosferatu (1922) [tmdbid-653] [imdbid-tt0013442]", 1922, "Nosferatu (1922)")]
    [InlineData("Step Up 3D (2010)", null, "Step Up 3D (2010)")]
    [InlineData("The Good German (2006) [tmdbid-182]", null, "The Good German (2006)")]
    [InlineData("[REC] 2 (2009) [tmdbid-10664]", null, "[REC] 2 (2009)")]
    [InlineData("Metropolis [1927]", null, "Metropolis (1927)")]
    [InlineData("Blade Runner 2049 (2017) [tmdbid-335984]", 2049, "Blade Runner 2049 (2017)")]
    [InlineData("Inception (2010) [1080p] [BluRay] [YTS.MX]", 2010, "Inception [1080p] [BluRay] [YTS.MX] (2010)")]
    [InlineData("(500) Days of Summer (2009) [tmdbid-19913]", null, "(500) Days of Summer (2009)")]
    [InlineData("(500) Days of Summer", 2009, "(500) Days of Summer (2009)")]
    [InlineData("Blade Runner 2049", 2017, "Blade Runner 2049 (2017)")]
    [InlineData("Halloween (2018)", 2018, "Halloween (2018)")]
    [InlineData("Le Voyage dans la Lune", 1902, "Le Voyage dans la Lune (1902)")]
    [InlineData("Sintel", null, "Sintel")]
    public void AMovieIsNamedByItsTitleAndItsYear(string name, int? year, string expected)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = name, ProductionYear = year };

        Assert.Equal(expected, Body(movie, new LocalizationHelper(null, null)));
    }

    /// <summary>
    /// The year goes through the administrator's wording for a name and its year, for a name that
    /// ended in its year too.
    /// </summary>
    /// <param name="name">The movie's name.</param>
    [Theory]
    [InlineData("Halloween")]
    [InlineData("Halloween (2018)")]
    public void TheYearGoesThroughTheAdministratorsWording(string name)
    {
        var localization = new LocalizationHelper(null, null, () => [new WordingOverride { Key = "NameAndYear", Text = "[{0}] / {1}" }]);
        var movie = new Movie { Id = Guid.NewGuid(), Name = name, ProductionYear = 2018 };

        Assert.Equal("[Halloween] / 2018", Body(movie, localization));
    }

    /// <summary>
    /// A batch of episodes names its show without the ids Sonarr writes in a folder name, which
    /// Jellyfin 10.11 names a new show after, and Jellyfin 12 too for a folder without a year. The
    /// rest of the name stays, a year in it included: the batch's message gives no year of its own.
    /// </summary>
    /// <param name="name">The show's name.</param>
    /// <param name="expected">What the message says.</param>
    [Theory]
    [InlineData("Breaking Bad (2008) [tvdbid-81189]", "Breaking Bad (2008) - Episode 2 added for Season 1")]
    [InlineData("Breaking Bad [tvdbid-81189]", "Breaking Bad - Episode 2 added for Season 1")]
    [InlineData("Mr  Robot (2015) [tvdbid-289590]", "Mr  Robot (2015) - Episode 2 added for Season 1")]
    [InlineData("Galactica 1980", "Galactica 1980 - Episode 2 added for Season 1")]
    [InlineData("Doctor Who (2005)", "Doctor Who (2005) - Episode 2 added for Season 1")]
    public void ABatchOfEpisodesNamesItsShowWithoutItsIds(string name, string expected)
    {
        var show = new Series { Id = Guid.NewGuid(), Name = name };
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

        Assert.Equal(expected, message.Body);
    }

    /// <summary>
    /// A message about an episode, the one an administrator gets when somebody starts it, names
    /// its show without the ids either.
    /// </summary>
    [Fact]
    public void AnEpisodeIsNamedByItsShowWithoutItsIds()
    {
        var series = new Series { Id = Guid.NewGuid(), Name = "Breaking Bad (2008) [tvdbid-81189]" };
        var episode = new Episode { Id = Guid.NewGuid(), SeriesId = series.Id, IndexNumber = 5 };

        var notification = WithLibrary(
            [series],
            () => MediaNotificationHelper.CreateMediaNotification(
                new LocalizationHelper(null, null),
                "Playback started",
                [],
                episode,
                CultureInfo.InvariantCulture));

        Assert.NotNull(notification);
        Assert.Equal("Breaking Bad (2008), Episode 05", notification.Body);
    }

    // The last line of a movie's message.
    private static string? Body(Movie movie, LocalizationHelper localization) =>
        WithLibrary(
            [movie],
            () => MediaNotificationHelper.CreateMediaNotification(
                localization,
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
