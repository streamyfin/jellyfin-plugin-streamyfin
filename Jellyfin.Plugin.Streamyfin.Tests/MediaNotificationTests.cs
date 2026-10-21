using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Emby.Naming.Common;
using Emby.Naming.Video;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
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
    /// A movie Jellyfin has not refreshed yet still has the name of its folder: its message gives
    /// the title and the year, without what Radarr writes there.
    /// </summary>
    /// <remarks>
    /// Seen on the beta with Radarr's folder naming: the TMDB id in the folder name gives the
    /// movie its id the moment it is added, so its message goes before the refresh, and named it
    /// "Nosferatu (1922) [tmdbid-653]".
    /// </remarks>
    [Fact]
    public void AMovieJellyfinHasNotRefreshedIsNamedByItsTitleAndYear()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Nosferatu (1922) [tmdbid-653]" };

        var notification = WithLibrary(
            [movie],
            () => MediaNotificationHelper.CreateMediaNotification(
                new LocalizationHelper(null, null),
                "Movie added",
                [],
                movie,
                CultureInfo.InvariantCulture));

        Assert.NotNull(notification);
        Assert.Equal("Nosferatu (1922)", notification.Body);
    }

    /// <summary>
    /// A movie Jellyfin has refreshed keeps the name its metadata gave it, even one ending in a
    /// number Jellyfin would read as a year in a folder name.
    /// </summary>
    [Fact]
    public void ARefreshedMovieKeepsTheNameItsMetadataGaveIt()
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = "Blade Runner 2049",
            ProductionYear = 2017,
            DateLastRefreshed = new DateTime(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc)
        };

        var notification = WithLibrary(
            [movie],
            () => MediaNotificationHelper.CreateMediaNotification(
                new LocalizationHelper(null, null),
                "Movie added",
                [],
                movie,
                CultureInfo.InvariantCulture));

        Assert.NotNull(notification);
        Assert.Equal("Blade Runner 2049 (2017)", notification.Body);
    }

    /// <summary>
    /// A movie with no year anywhere, neither in its folder name nor in its metadata, is named
    /// alone, without empty brackets.
    /// </summary>
    [Fact]
    public void AMovieWithoutAYearIsNamedAlone()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Sintel" };

        var notification = WithLibrary(
            [movie],
            () => MediaNotificationHelper.CreateMediaNotification(
                new LocalizationHelper(null, null),
                "Movie added",
                [],
                movie,
                CultureInfo.InvariantCulture));

        Assert.NotNull(notification);
        Assert.Equal("Sintel", notification.Body);
    }

    /// <summary>
    /// A batch of episodes of a show Jellyfin has not refreshed yet names the show by its title,
    /// without the year and the id Sonarr writes in its folder name.
    /// </summary>
    [Fact]
    public void AShowJellyfinHasNotRefreshedIsNamedByItsTitle()
    {
        var show = new Series { Id = Guid.NewGuid(), Name = "Breaking Bad (2008) [tvdbid-81189]" };

        var name = WithLibrary([show], () => ItemAddedService.ShowName(show));

        Assert.Equal("Breaking Bad", name);
    }

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
    /// An <see cref="ILibraryManager"/> that knows a few items by id, parses names, and nothing
    /// else.
    /// </summary>
    public class LibraryStub : DispatchProxy
    {
        private static readonly NamingOptions Naming = new();

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

            // What Jellyfin's LibraryManager.ParseName does, with the naming rules it ships.
            if (targetMethod?.Name == nameof(ILibraryManager.ParseName) && args is [string name])
            {
                var dated = VideoResolver.CleanDateTime(name, Naming);
                return new ItemLookupInfo
                {
                    Name = VideoResolver.TryCleanString(dated.Name, Naming, out var cleaned) ? cleaned : dated.Name,
                    Year = dated.Year
                };
            }

            var returns = targetMethod?.ReturnType;
            return returns is { IsValueType: true } && returns != typeof(void) ? Activator.CreateInstance(returns) : null;
        }
    }
}
