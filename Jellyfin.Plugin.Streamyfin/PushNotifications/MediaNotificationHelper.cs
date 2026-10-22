using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Streamyfin.Extensions;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

static partial class MediaNotificationHelper
{
    /// <summary>
    /// A title's name without the ids Jellyfin reads in a folder name, for a message that gives
    /// no year: a show in a batch of episodes, or the show of an episode somebody started.
    /// </summary>
    /// <param name="title">The movie or the show.</param>
    /// <returns>The name.</returns>
    /// <remarks>
    /// Until its metadata names it, Jellyfin names a new title after its folder, with the ids
    /// Radarr or Sonarr write there, such as <c>[tmdbid-653]</c>. A new item message waits for
    /// the name, two minutes at most, and goes with the folder's when Jellyfin did not name the
    /// title in time. Such an id is never part of a title, so it comes off any name, and nothing
    /// else does. A name made of ids alone is kept as it is rather than left empty.
    /// </remarks>
    internal static string NameOf(BaseItem title)
    {
        var name = Ids().Replace(title.Name ?? string.Empty, string.Empty).Trim();

        return name.Length > 0 ? name : title.Name ?? string.Empty;
    }

    /// <summary>
    /// A title's name and year, for a message that gives the year: a movie, or a show's season.
    /// </summary>
    /// <param name="title">The movie or the show.</param>
    /// <returns>Its name without its ids, and its year when one is known.</returns>
    /// <remarks>
    /// The year a name carries in brackets is given once, in the year's place: a year at the end
    /// of the name, a folder's "(1922)" or "[1922]" that Radarr wrote, is the year, over one
    /// Jellyfin read off the file name; elsewhere in the name, a bracketed year that is the
    /// title's own comes off, "Inception (2010) [1080p]". Any other bracket stays.
    /// </remarks>
    internal static (string Name, int? Year) NameAndYearOf(BaseItem title)
    {
        var name = NameOf(title);
        var last = TrailingYear().Match(name);

        if (last.Success && last.Index > 0)
        {
            return (name[..last.Index], int.Parse(last.Groups["year"].ValueSpan, CultureInfo.InvariantCulture));
        }

        if (title.ProductionYear is not { } year)
        {
            return (name, null);
        }

        var own = year.ToString(CultureInfo.InvariantCulture);
        var once = BracketedYear().Replace(name, found => found.Groups["year"].Value == own ? string.Empty : found.Value).Trim();

        return (once.Length > 0 ? once : name, year);
    }

    // The ids Jellyfin reads in a folder name, with the short forms Jellyfin 12 also reads.
    private const string IdNames = "(?:tmdb|tvdb|imdb)(?:id)?|tvmazeid|anidbid|anilistid|anisearchid";

    // Each of them in every bracket Jellyfin 12 reads it in, with the space before it:
    // " [tmdbid-653]", " [tmdbid=653]", " {tmdb-653}", " (imdbid-tt0013442)" and the like.
    [GeneratedRegex(
        @"\s*(?:\[(?:" + IdNames + @")[=-][^\]]+\]|\((?:" + IdNames + @")[=-][^)]+\)|\{(?:" + IdNames + @")[=-][^}]+\})",
        RegexOptions.IgnoreCase)]
    private static partial Regex Ids();

    // A year in round or square brackets, with the space before it, as Jellyfin's naming rules
    // read one in a folder name: 19xx or 20xx.
    [GeneratedRegex(@"\s*[(\[](?<year>(?:19|20)[0-9]{2})[)\]]")]
    private static partial Regex BracketedYear();

    // The same at the very end of a name.
    [GeneratedRegex(@"\s*[(\[](?<year>(?:19|20)[0-9]{2})[)\]]$")]
    private static partial Regex TrailingYear();

    public static ExpoNotificationRequest? CreateMediaNotification(
        LocalizationHelper localization,
        string title,
        List<string> body,
        BaseItem item,
        CultureInfo? culture = null)
    {
        string? name = null;
        var data = new Dictionary<string, object?>();

        switch (item)
        {
            case Movie movie:
                var (named, year) = NameAndYearOf(movie);
                var movieName = named.Escape();
                data["id"] = movie.Id.ToString("N");

                name = year is null
                    ? movieName
                    : localization.GetFormatted(
                        key: "NameAndYear",
                        cultureInfo: culture,
                        args: [movieName, year]
                    );
                break;
            case Season season:
                if (season.Series is { } show && !string.IsNullOrEmpty(show.Name))
                {
                    var (showName, showYear) = NameAndYearOf(show);
                    name = showYear is null
                        ? showName.Escape()
                        : localization.GetFormatted(
                            key: "NameAndYear",
                            cultureInfo: culture,
                            args: [showName.Escape(), showYear]
                        );
                }

                if (season.Series?.Id is not null)
                {
                    data["seriesId"] = season.Series.Id.ToString("N");
                }

                break;
            case Episode episode:
                data["id"] = episode.Id.ToString("N");

                name = !string.IsNullOrEmpty(episode.Series?.Name) switch
                {
                    // Name + Season + Episode
                    true when episode.Season?.IndexNumber is not null && episode.IndexNumber is not null =>
                        localization.GetFormatted(
                            key: "SeriesSeasonAndEpisode",
                            cultureInfo: culture,
                            args:
                            [
                                NameOf(episode.Series).Escape(),
                                episode.Season.IndexNumber.Value.ToString(CultureInfo.InvariantCulture),
                                episode.IndexNumber.Value.ToString("00", CultureInfo.InvariantCulture)
                            ]
                        ),
                    // Name + Season
                    true when episode.Season?.IndexNumber is not null =>
                        localization.GetFormatted(
                            key: "SeriesSeason",
                            cultureInfo: culture,
                            args:
                            [
                                NameOf(episode.Series).Escape(),
                                episode.Season.IndexNumber.Value.ToString(CultureInfo.InvariantCulture)
                            ]
                        ),
                    // Name + Episode
                    true when episode.IndexNumber is not null =>
                        localization.GetFormatted(
                            key: "Series Episode",
                            cultureInfo: culture,
                            args:
                            [
                                NameOf(episode.Series).Escape(),
                                episode.IndexNumber.Value.ToString("00", CultureInfo.InvariantCulture)
                            ]
                        ),
                    _ => episode.Series?.Name.Escape()
                };

                data["seriesId"] = episode.SeriesId.ToString("N");
                data["seasonIndex"] = episode.Season?.IndexNumber;

                break;
        }
        
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        body.Add(name);
        data["type"] = item.GetType().Name.Escape();

        return new ExpoNotificationRequest
        {
            Title = title,
            Body = string.Join("\n", body),
            Data = data
        };
    }
}