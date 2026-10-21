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

static class MediaNotificationHelper
{
    /// <summary>
    /// The name and the year a message gives a movie or a show.
    /// </summary>
    /// <param name="title">The movie or the show.</param>
    /// <returns>Its name, and its year when one is known.</returns>
    /// <remarks>
    /// Until its first refresh, Jellyfin names a title after its folder or file, with the year
    /// and what Radarr or Sonarr write there, such as <c>[tmdbid-653]</c>. A message can go in
    /// that moment: with the id in the folder name, nothing holds it for the refresh. Such a name
    /// is read the way Jellyfin reads it to look the title up. A refreshed title keeps the name
    /// its metadata gave it, which may end in a number that is no year.
    /// </remarks>
    internal static (string Name, int? Year) NameAndYearOf(BaseItem title)
    {
        if (title.DateLastRefreshed != default)
        {
            return (title.Name, title.ProductionYear);
        }

        var parsed = BaseItem.LibraryManager?.ParseName(title.Name);

        return (string.IsNullOrWhiteSpace(parsed?.Name) ? title.Name : parsed.Name, title.ProductionYear ?? parsed?.Year);
    }

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
                // Potentially clean up any BaseItem without any corrected metadata
                var movieName = Regex.Replace(named.Escape(), "\\(\\d+\\)", "").Trim();
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