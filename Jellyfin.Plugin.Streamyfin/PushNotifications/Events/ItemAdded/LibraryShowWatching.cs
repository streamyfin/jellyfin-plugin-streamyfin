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
    /// Played rather than started: Jellyfin marks an episode played when it ends, which is the
    /// rule the For you row already follows.
    /// </remarks>
    public bool HasStarted(User user, Guid seriesId) =>
        libraries.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            AncestorIds = [seriesId],
            IsPlayed = true,
            Recursive = true,
            Limit = 1,
            DtoOptions = new DtoOptions(false)
        }).Count > 0;
}
