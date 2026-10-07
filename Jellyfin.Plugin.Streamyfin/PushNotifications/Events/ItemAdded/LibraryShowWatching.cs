using System;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;

/// <summary>
/// Whether somebody follows a show, read from what Jellyfin knows of them.
/// </summary>
/// <param name="libraries">The library, for the show and its episodes.</param>
/// <param name="userData">Each person's marks on an item.</param>
internal sealed class LibraryShowWatching(ILibraryManager libraries, IUserDataManager userData) : IShowWatching
{
    /// <inheritdoc/>
    public bool IsFavorite(User user, Guid seriesId) =>
        libraries.GetItemById(seriesId) is { } series
        && userData.GetUserData(user, series)?.IsFavorite == true;

    /// <inheritdoc/>
    /// <remarks>
    /// Watched or begun: Jellyfin marks an episode played only near its end, and an episode
    /// left halfway already makes the show one they follow. Two questions, since one query
    /// asking for both would ask for an episode that is both.
    /// </remarks>
    public bool HasStarted(User user, Guid seriesId) =>
        AnyEpisode(user, seriesId, query => query.IsPlayed = true)
        || AnyEpisode(user, seriesId, query => query.IsResumable = true);

    /// <summary>
    /// Whether the person has an episode of the show that the query picks.
    /// </summary>
    private bool AnyEpisode(User user, Guid seriesId, Action<InternalItemsQuery> which)
    {
        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            AncestorIds = [seriesId],
            Recursive = true,
            Limit = 1,
            DtoOptions = new DtoOptions(false)
        };
        which(query);
        return libraries.GetItemList(query).Count > 0;
    }
}
