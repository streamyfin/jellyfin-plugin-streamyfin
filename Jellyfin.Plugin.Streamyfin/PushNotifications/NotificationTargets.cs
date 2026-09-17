using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.Db;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// Who each event reaches, once the groups and the users have had their say.
/// </summary>
/// <remarks>
/// Read once per send rather than per user: deciding who an event goes to asks about every
/// account on the server, and the levels are three small tables.
/// </remarks>
public sealed class NotificationTargets
{
    private readonly Dictionary<Guid, List<IReadOnlyDictionary<string, NotificationTargeting>?>> _levels;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationTargets"/> class.
    /// </summary>
    /// <param name="stored">
    /// What each level says about each user, as stored: the groups they are in, least
    /// specific first, then their own.
    /// </param>
    public NotificationTargets(IReadOnlyDictionary<Guid, List<string>> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        _levels = stored.ToDictionary(
            said => said.Key,
            said => said.Value.Select(NotificationTargeting.Read).ToList());
    }

    /// <summary>
    /// The levels as the database holds them.
    /// </summary>
    /// <param name="database">The plugin's database, which may not be open yet.</param>
    /// <returns>What every level says, ready to be asked about a user.</returns>
    public static NotificationTargets From(PluginDatabase? database) =>
        new(database?.NotificationLevels() ?? []);

    /// <summary>
    /// Whether an event reaches someone.
    /// </summary>
    /// <param name="eventKey">The event, by the key the configuration and the page use.</param>
    /// <param name="userId">The account.</param>
    /// <param name="serverEnabled">Whether the server has the event on at all.</param>
    /// <param name="inDefaultAudience">Whether they are who the event is for by default.</param>
    /// <returns>Whether to send it to their devices.</returns>
    public bool Reaches(string eventKey, Guid userId, bool serverEnabled, bool inDefaultAudience) =>
        NotificationTargeting.Reaches(
            eventKey,
            serverEnabled,
            inDefaultAudience,
            _levels.TryGetValue(userId, out var said) ? said : []);
}
