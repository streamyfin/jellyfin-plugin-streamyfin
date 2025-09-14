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
    public static ExpoNotificationRequest? CreateMediaNotification(
        LocalizationHelper localization,
        string title,
        List<string> body, 
        BaseItem item)
    {
        string? name = null;
        var data = new Dictionary<string, object?>();

        switch (item)
        {
            case Movie movie:
                // Potentially clean up any BaseItem without any corrected metadata
                var movieName = Regex.Replace(movie.Name.Escape(), "\\(\\d+\\)", "").Trim();
                data["id"] = movie.Id.ToString("N");

                name = localization.GetFormatted(
                    key: "NameAndYear",
                    args: [movieName, movie.ProductionYear]
                );
                break;
            case Season season:
                if (!string.IsNullOrEmpty(season.Series.Name) && season.Series?.ProductionYear is not null)
                {
                    name = localization.GetFormatted(
                        key: "NameAndYear",
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
                            args:
                            [
                                episode.Series.Name.Escape(),
                                episode.Season.IndexNumber.Value.ToString(CultureInfo.InvariantCulture)
                            ]
                        ),
                    // Name + Episode
                    true when episode.IndexNumber is not null =>
                        localization.GetFormatted(
                            key: "SeriesEpisode",
                            args:
                            [
                                episode.Series.Name.Escape(),
                                episode.IndexNumber?.ToString("00", CultureInfo.InvariantCulture) ?? string.Empty
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