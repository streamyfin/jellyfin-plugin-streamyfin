using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    /// The name and the year a message gives a movie or a show.
    /// </summary>
    /// <param name="title">The movie or the show.</param>
    /// <returns>Its name, and its year when one is known.</returns>
    /// <remarks>
    /// Until its metadata names it, Jellyfin names a movie in a folder of its own after that
    /// folder, word for word, and Jellyfin 10.11 does the same with a show: with the year and the
    /// ids Radarr or Sonarr write there, such as <c>[tmdbid-653]</c>. A message can go in that
    /// moment, since with the id in the folder name nothing holds it for the refresh. Only such a
    /// name is cleaned, and only of what Jellyfin itself reads in a folder name: the ids, and the
    /// year at the end, which is then the year. Any other name is a title already, the
    /// metadata's or one Jellyfin read off a file name, kept whole but for a year the message
    /// would repeat.
    /// </remarks>
    internal static (string Name, int? Year) NameAndYearOf(BaseItem title)
    {
        var name = title.Name ?? string.Empty;
        var year = title.ProductionYear;
        var folderName = HasFolderName(title);

        if (folderName)
        {
            name = Spaces().Replace(FolderIds().Replace(name, " "), " ").Trim();
        }

        var dated = TrailingYear().Match(name);
        if (dated.Success
            && (folderName || year?.ToString(CultureInfo.InvariantCulture) == dated.Groups["year"].Value)
            && dated.Index > 0)
        {
            year = int.Parse(dated.Groups["year"].ValueSpan, CultureInfo.InvariantCulture);
            name = name[..dated.Index];
        }

        return name.Length > 0 ? (name, year) : (title.Name ?? string.Empty, title.ProductionYear);
    }

    // Whether a title still has the name Jellyfin gave it from its folder: a movie in a folder of
    // its own, or a show, named exactly as that folder is.
    private static bool HasFolderName(BaseItem title)
    {
        if (title.IsInMixedFolder || string.IsNullOrEmpty(title.ContainingFolderPath))
        {
            return false;
        }

        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(title.ContainingFolderPath));

        return string.Equals(title.Name, folder, StringComparison.Ordinal);
    }

    // The ids Jellyfin reads in a folder name, with the short forms Jellyfin 12 also reads.
    private const string IdNames = "(?:tmdb|tvdb|imdb)(?:id)?|tvmazeid|anidbid|anilistid|anisearchid";

    // Each of them in every bracket Jellyfin 12 reads it in: [tmdbid-653], [tmdbid=653],
    // {tmdb-653}, (imdbid-tt0013442) and the like.
    [GeneratedRegex(
        @"\[(?:" + IdNames + @")[=-][^\]]+\]|\((?:" + IdNames + @")[=-][^)]+\)|\{(?:" + IdNames + @")[=-][^}]+\}",
        RegexOptions.IgnoreCase)]
    private static partial Regex FolderIds();

    // A year in brackets at the end of a name, as Radarr and Sonarr write it.
    [GeneratedRegex(@"\s*\((?<year>[0-9]{4})\)\s*$")]
    private static partial Regex TrailingYear();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

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
                if (!string.IsNullOrEmpty(season.Series.Name) && season.Series?.ProductionYear is not null)
                {
                    name = localization.GetFormatted(
                        key: "NameAndYear",
                        cultureInfo: culture,
                        args: [season.Series.Name.Escape(), season.Series.ProductionYear]
                    );
                }
                else if (!string.IsNullOrEmpty(season.Series?.Name))
                {
                    name = season.Series.Name.Escape();
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
                                episode.Series.Name.Escape(),
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
                                episode.Series.Name.Escape(),
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
                                episode.Series.Name.Escape(),
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