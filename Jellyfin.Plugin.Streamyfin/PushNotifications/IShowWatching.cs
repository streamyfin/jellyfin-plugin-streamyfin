using System;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// What a person does with a show, which decides whether they follow it.
/// </summary>
public interface IShowWatching
{
    /// <summary>Whether they marked the show as favorite.</summary>
    /// <param name="user">The person.</param>
    /// <param name="seriesId">The show.</param>
    /// <returns>True for a favorite.</returns>
    bool IsFavorite(User user, Guid seriesId);

    /// <summary>Whether they watched or began at least one episode of it.</summary>
    /// <param name="user">The person.</param>
    /// <param name="seriesId">The show.</param>
    /// <returns>True once an episode is played or in progress.</returns>
    bool HasStarted(User user, Guid seriesId);
}
