using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.Db;

namespace Jellyfin.Plugin.Streamyfin.Api;

/// <summary>
/// Everything an administrator set, in one file.
/// </summary>
/// <remarks>
/// The configuration alone is not the work: the targeting levels are, and they live in
/// the plugin's own database rather than in Jellyfin's XML, so nothing a server
/// administrator backs up today carries them.
///
/// <para>
/// It carries the credentials the configuration carries, because a backup that cannot
/// restore a working server is not one. Both routes are for administrators, and the
/// page says so before it hands the file over.
/// </para>
/// </remarks>
public class ConfigurationBackup
{
    /// <summary>
    /// Gets or sets the plugin version that wrote this.
    /// </summary>
    [JsonPropertyName("plugin")]
    public string Plugin { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when it was taken.
    /// </summary>
    [JsonPropertyName("takenAt")]
    public DateTimeOffset TakenAt { get; set; }

    /// <summary>
    /// Gets or sets the configuration, which is everything the Yaml tab edits.
    /// </summary>
    [JsonPropertyName("config")]
    public Configuration.Config? Config { get; set; }

    /// <summary>
    /// Gets or sets the settings groups, with their members.
    /// </summary>
    [JsonPropertyName("groups")]
    public List<SettingsGroupDto> Groups { get; set; } = [];

    /// <summary>
    /// Gets or sets the settings targeted at one user each.
    /// </summary>
    [JsonPropertyName("users")]
    public List<UserBackup> Users { get; set; } = [];

    /// <summary>
    /// Gets or sets what each person chose for their own notifications, or <c>null</c> in a
    /// backup taken before they could, which a restore then leaves as they are.
    /// </summary>
    [JsonPropertyName("notificationPreferences")]
    public List<PreferencesBackup>? NotificationPreferences { get; set; }

    /// <summary>
    /// Gets or sets the titles each person waits for, or <c>null</c> in a backup taken before
    /// they could, which a restore then leaves as they are (#225).
    /// </summary>
    [JsonPropertyName("awaitedTitles")]
    public List<AwaitedTitleBackup>? Awaited { get; set; }

    /// <summary>
    /// What makes the awaited titles in this file impossible to restore, found before anything
    /// is written.
    /// </summary>
    /// <returns>A sentence for the page, or <c>null</c> when they can be restored.</returns>
    public string? AwaitedProblem()
    {
        if (Awaited is not { } titles)
        {
            return null;
        }

        if (titles.Any(title => title is null))
        {
            return "One of the awaited titles in this file is empty.";
        }

        foreach (var title in titles)
        {
            if (Configuration.Notifications.AwaitedTitles.Problem(title.MediaType, title.TmdbId, title.TvdbId, title.Title) is { } problem)
            {
                return $"An awaited title of user {title.UserId} in this file cannot be restored. {problem}";
            }
        }

        if (titles.GroupBy(title => (title.UserId, title.MediaType, title.TmdbId)).FirstOrDefault(same => same.Count() > 1) is { } twice)
        {
            return $"This file has the same awaited title twice for user {twice.Key.UserId}.";
        }

        return titles.GroupBy(title => title.UserId).FirstOrDefault(one => one.Count() > Configuration.Notifications.AwaitedTitles.MostAwaited) is { } full
            ? $"User {full.Key} waits for more than {Configuration.Notifications.AwaitedTitles.MostAwaited} titles in this file."
            : null;
    }

    /// <summary>
    /// What makes the groups and the user settings in this file impossible to restore, found
    /// before anything is written. The server holds one group per id and per name, and one
    /// entry per user, so a file that repeats one would stop the restore partway through.
    /// </summary>
    /// <returns>A sentence for the page, or <c>null</c> when they can be restored.</returns>
    public string? TargetingProblem()
    {
        if (Groups.Any(group => group is null))
        {
            return "One of the groups in this file is empty.";
        }

        if (Users.Any(user => user is null))
        {
            return "One of the user settings in this file is empty.";
        }

        if (Groups.Any(group => string.IsNullOrWhiteSpace(group.Name)))
        {
            return "Every group in a backup needs a name, and one of these has none.";
        }

        // A group without an id is given a new one, so only an id the file repeats clashes.
        if (Groups
            .Where(group => group.Id != Guid.Empty)
            .GroupBy(group => group.Id)
            .FirstOrDefault(same => same.Count() > 1) is { } sameId)
        {
            return $"This file has two groups with the id {sameId.Key}.";
        }

        // Compared the way the database compares them, where "Kids" and "kids" are two names.
        if (Groups
            .GroupBy(group => group.Name, StringComparer.Ordinal)
            .FirstOrDefault(same => same.Count() > 1) is { } sameName)
        {
            return $"This file has two groups named \"{sameName.Key}\".";
        }

        if (Users.GroupBy(user => user.UserId).FirstOrDefault(same => same.Count() > 1) is { } sameUser)
        {
            return $"This file has the settings of user {sameUser.Key} twice.";
        }

        return null;
    }

    /// <summary>
    /// What makes what a group or a user in this file says about the events impossible to
    /// restore, checked the way the pages' routes check it.
    /// </summary>
    /// <param name="known">
    /// The users this server has. A restore leaves anyone else out, so what they say is not
    /// checked: a file from another server is not refused over a user it would not write.
    /// </param>
    /// <returns>A sentence for the page that names the level, or <c>null</c>.</returns>
    public string? NotificationsProblem(IReadOnlySet<Guid> known)
    {
        ArgumentNullException.ThrowIfNull(known);

        foreach (var group in Groups)
        {
            if (NotificationsValidation.CheckTargeting(group.Notifications) is { } problem)
            {
                return $"The group \"{group.Name}\" in this file cannot be restored. {problem}";
            }
        }

        foreach (var user in Users.Where(user => known.Contains(user.UserId)))
        {
            if (NotificationsValidation.CheckTargeting(user.Notifications) is { } problem)
            {
                return $"The settings of user {user.UserId} in this file cannot be restored. {problem}";
            }
        }

        return null;
    }

    /// <summary>
    /// The rows a restore writes for the groups and the user settings in this file.
    /// </summary>
    /// <param name="serialization">Writes a level's settings the way the plugin stores them.</param>
    /// <param name="known">The users this server has. Anyone else is left out, and counted.</param>
    /// <returns>The rows, and how many members and users were left out.</returns>
    /// <remarks>
    /// Each level carries what it says about the events next to its settings. Writing the
    /// settings alone took every group's and every user's notifications away on a restore.
    /// </remarks>
    public TargetingRows ToRows(SerializationHelper serialization, IReadOnlySet<Guid> known)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(known);

        var unknownMembers = 0;
        var groups = new List<(SettingsGroup Group, IReadOnlyList<Guid> Members)>();

        foreach (var group in Groups)
        {
            var members = (group.UserIds ?? []).Where(known.Contains).ToList();
            unknownMembers += (group.UserIds?.Count ?? 0) - members.Count;

            groups.Add((
                new SettingsGroup
                {
                    Id = group.Id,
                    Name = group.Name,
                    Priority = group.Priority,
                    SettingsJson = serialization.SerializeToJson(group.Settings ?? new Configuration.Settings.Settings()),
                    NotificationsJson = NotificationTargeting.Write(group.Notifications)
                },
                members));
        }

        var users = Users
            .Where(user => known.Contains(user.UserId))
            .Select(user => new UserSettingsOverride
            {
                UserId = user.UserId,
                SettingsJson = serialization.SerializeToJson(user.Settings ?? new Configuration.Settings.Settings()),
                NotificationsJson = NotificationTargeting.Write(user.Notifications)
            })
            .ToList();

        return new TargetingRows(groups, users, unknownMembers, Users.Count - users.Count);
    }

    /// <summary>
    /// What makes the choices in this file impossible to restore, found before anything is
    /// written. A choice that leaves out what the person chose is as empty as a missing one:
    /// the restore would otherwise take that person's choices away. A restore keeps one entry
    /// per person, and holds each to the same most as the app.
    /// </summary>
    /// <returns>A sentence for the page, or <c>null</c> when they can be restored.</returns>
    public string? PreferencesProblem()
    {
        if (NotificationPreferences is not { } choices)
        {
            return null;
        }

        if (choices.Any(choice => choice?.Preferences is null))
        {
            return "One of the notification choices in this file is empty.";
        }

        if (choices.GroupBy(choice => choice!.UserId).Any(same => same.Count() > 1))
        {
            return "This file has the notification choices of one person twice.";
        }

        return choices
            .Select(choice => MyNotifications.ListsProblem(
                choice!.Preferences!.MutedShows?.Count ?? 0,
                choice.Preferences.MutedLibraries?.Count ?? 0))
            .FirstOrDefault(problem => problem is not null);
    }
}

/// <summary>
/// The settings targeted at one user.
/// </summary>
public class UserBackup
{
    /// <summary>
    /// Gets or sets the Jellyfin user.
    /// </summary>
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets what is targeted at them.
    /// </summary>
    [JsonPropertyName("settings")]
    public Configuration.Settings.Settings? Settings { get; set; }

    /// <summary>
    /// Gets or sets what is targeted at them about the events, or <c>null</c> when nothing
    /// is, as in a backup taken before a user could be told anything about them.
    /// </summary>
    [JsonPropertyName("notifications")]
    public Dictionary<string, NotificationTargeting>? Notifications { get; set; }
}

/// <summary>
/// The rows a restore writes in place of the groups and the user settings on a server.
/// </summary>
/// <param name="Groups">Each group, with the members this server has.</param>
/// <param name="Users">What is targeted at each user this server has.</param>
/// <param name="UnknownMembers">How many members the file names that this server does not have.</param>
/// <param name="UnknownUsers">How many users the file names that this server does not have.</param>
public sealed record TargetingRows(
    IReadOnlyList<(SettingsGroup Group, IReadOnlyList<Guid> Members)> Groups,
    IReadOnlyList<UserSettingsOverride> Users,
    int UnknownMembers,
    int UnknownUsers);

/// <summary>
/// One person's notification choices in a backup.
/// </summary>
public class PreferencesBackup
{
    /// <summary>
    /// Gets or sets the Jellyfin user.
    /// </summary>
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets their choices.
    /// </summary>
    [JsonPropertyName("preferences")]
    public NotificationPreferences? Preferences { get; set; }
}

/// <summary>
/// A title one person waits for, in a backup (#225).
/// </summary>
public class AwaitedTitleBackup
{
    /// <summary>Gets or sets the Jellyfin user.</summary>
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the kind of title.</summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    /// <summary>Gets or sets its TMDB id.</summary>
    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    /// <summary>Gets or sets its TVDB id.</summary>
    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; }

    /// <summary>Gets or sets its name.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the year it came out.</summary>
    [JsonPropertyName("year")]
    public int? Year { get; set; }

    /// <summary>Gets or sets when the person asked, in UTC.</summary>
    [JsonPropertyName("addedAt")]
    public DateTime AddedAt { get; set; }

    /// <summary>Gets or sets what arrived during their pause, when something did.</summary>
    [JsonPropertyName("arrivedItemId")]
    public Guid? ArrivedItemId { get; set; }

    /// <summary>Takes a stored row into a backup.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The backup entry.</returns>
    public static AwaitedTitleBackup From(Db.AwaitedTitle row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new AwaitedTitleBackup
        {
            UserId = row.UserId,
            MediaType = row.MediaType,
            TmdbId = row.TmdbId,
            TvdbId = row.TvdbId,
            Title = row.Title,
            Year = row.Year,
            AddedAt = row.AddedAt,
            ArrivedItemId = row.ArrivedItemId
        };
    }

    /// <summary>The row a restore writes. Checked before, with <c>AwaitedProblem</c>.</summary>
    /// <returns>The row.</returns>
    public Db.AwaitedTitle ToRow() => new()
    {
        UserId = UserId,
        MediaType = MediaType ?? string.Empty,
        TmdbId = TmdbId,
        TvdbId = TvdbId,
        Title = (Title ?? string.Empty).Trim(),
        Year = Year,
        AddedAt = AddedAt,
        ArrivedItemId = ArrivedItemId
    };
}

/// <summary>
/// What a restore did.
/// </summary>
/// <remarks>
/// A backup taken on one server and restored on another names users that server has
/// never heard of. Those are skipped rather than refused, since the rest of the file is
/// still worth having, and counted so an administrator is told rather than left to
/// notice.
/// </remarks>
public class RestoreReport
{
    /// <summary>
    /// Gets or sets whether the configuration was replaced.
    /// </summary>
    [JsonPropertyName("configuration")]
    public bool Configuration { get; set; }

    /// <summary>
    /// Gets or sets how many groups were restored.
    /// </summary>
    [JsonPropertyName("groups")]
    public int Groups { get; set; }

    /// <summary>
    /// Gets or sets how many users were given their settings back.
    /// </summary>
    [JsonPropertyName("users")]
    public int Users { get; set; }

    /// <summary>
    /// Gets or sets how many group members this server does not have.
    /// </summary>
    [JsonPropertyName("unknownMembers")]
    public int UnknownMembers { get; set; }

    /// <summary>
    /// Gets or sets how many users the file targets that this server does not have.
    /// </summary>
    [JsonPropertyName("unknownUsers")]
    public int UnknownUsers { get; set; }

    /// <summary>
    /// Gets or sets how many people had their own notification choices put back.
    /// </summary>
    [JsonPropertyName("preferences")]
    public int Preferences { get; set; }

    /// <summary>
    /// Gets or sets how many awaited titles were put back.
    /// </summary>
    [JsonPropertyName("awaited")]
    public int Awaited { get; set; }

    /// <summary>
    /// Gets or sets what stopped the restore, when something did.
    /// </summary>
    [JsonPropertyName("problem")]
    public string? Problem { get; set; }
}
