using System;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Notifications;

/// <summary>
/// What a person keeps of an event their levels already sent them (P4.5).
/// </summary>
/// <remarks>
/// Asked after <see cref="NotificationTargeting.Reaches"/> and never instead of it: the person
/// narrows what the server, their groups and an administrator decided, and cannot widen it.
/// </remarks>
public static class PersonalRule
{
    /// <summary>
    /// Whether a person keeps a message.
    /// </summary>
    /// <param name="mine">What they chose, or <c>null</c> when they chose nothing.</param>
    /// <param name="subject">What the message is about.</param>
    /// <param name="nowUtc">The moment it is sent, in UTC.</param>
    /// <param name="follows">Whether they follow a show, asked only for new episodes.</param>
    /// <returns>True when the message goes to their devices.</returns>
    public static bool Keeps(
        NotificationPreferences? mine,
        NotificationSubject subject,
        DateTime nowUtc,
        Func<Guid, bool> follows)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(follows);

        if (mine is null)
        {
            return true;
        }

        if (mine.IsPaused(nowUtc))
        {
            return false;
        }

        if (subject.SeriesId is { } series)
        {
            if (mine.MutedShows.Contains(series))
            {
                return false;
            }

            // A followed show notifies even with new items off or its library unticked: that
            // is the point of following it.
            if (follows(series))
            {
                return true;
            }
        }

        if (!mine.Keeps(subject.EventKey))
        {
            return false;
        }

        return subject.LibraryId is not { } library || !mine.MutedLibraries.Contains(library);
    }

    /// <summary>
    /// Whether a show counts as followed, the way the person said.
    /// </summary>
    /// <param name="choice">Which shows count.</param>
    /// <param name="isFavorite">Whether they marked the show as favorite.</param>
    /// <param name="hasStarted">Whether they watched or began an episode of it, asked last.</param>
    /// <returns>True when the show is followed.</returns>
    public static bool Follows(FollowChoice choice, bool isFavorite, Func<bool> hasStarted)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(hasStarted);

        return (choice.Favorites && isFavorite) || (choice.Started && hasStarted());
    }
}
