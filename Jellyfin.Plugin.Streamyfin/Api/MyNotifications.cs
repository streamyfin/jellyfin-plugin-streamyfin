using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Streamyfin.Api;

/// <summary>What a person gets, and what they kept of it, as the app reads it.</summary>
public sealed class MyNotificationsDto
{
    /// <summary>Gets or sets the pause, or <c>null</c>.</summary>
    [JsonPropertyName("pause")]
    public NotificationPause? Pause { get; set; }

    /// <summary>Gets or sets the events that can reach them, in the app's order.</summary>
    [JsonPropertyName("events")]
    public List<MyEventDto> Events { get; set; } = [];

    /// <summary>Gets or sets the libraries they can open, for new items.</summary>
    [JsonPropertyName("libraries")]
    public List<MyLibraryDto> Libraries { get; set; } = [];

    /// <summary>Gets or sets which shows count as followed.</summary>
    [JsonPropertyName("follow")]
    public FollowChoice Follow { get; set; } = new();

    /// <summary>Gets or sets the shows they turned off, by name.</summary>
    [JsonPropertyName("mutedShows")]
    public List<MyShowDto> MutedShows { get; set; } = [];
}

/// <summary>One event that can reach a person.</summary>
public sealed class MyEventDto
{
    /// <summary>Gets or sets the event key.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets its family, which is its Android channel.</summary>
    [JsonPropertyName("family")]
    public string Family { get; set; } = string.Empty;

    /// <summary>Gets or sets whether they keep it.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

/// <summary>One library a person can open.</summary>
public sealed class MyLibraryDto
{
    /// <summary>Gets or sets the library id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    /// <summary>Gets or sets its name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets whether its new items are announced to them.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

/// <summary>A show a person turned off.</summary>
public sealed class MyShowDto
{
    /// <summary>Gets or sets the show id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    /// <summary>Gets or sets its name, or its id when the server no longer has it.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>What the app sends to replace a person's choices.</summary>
public sealed class MyNotificationsUpdate
{
    /// <summary>Gets or sets the pause, or <c>null</c> for none.</summary>
    [JsonPropertyName("pause")]
    public NotificationPause? Pause { get; set; }

    /// <summary>Gets or sets the events turned on or off.</summary>
    [JsonPropertyName("events")]
    public Dictionary<string, bool>? Events { get; set; }

    /// <summary>Gets or sets the libraries turned off.</summary>
    [JsonPropertyName("mutedLibraries")]
    public List<Guid>? MutedLibraries { get; set; }

    /// <summary>Gets or sets which shows count as followed.</summary>
    [JsonPropertyName("follow")]
    public FollowChoice? Follow { get; set; }

    /// <summary>Gets or sets the shows turned off.</summary>
    [JsonPropertyName("mutedShows")]
    public List<Guid>? MutedShows { get; set; }
}

/// <summary>A pause asked from a notification's button or the app.</summary>
public sealed class PauseRequest
{
    /// <summary>Gets or sets how many hours, or <c>null</c> until it is lifted.</summary>
    [JsonPropertyName("hours")]
    public int? Hours { get; set; }
}

/// <summary>Reads and writes a person's choices for the app.</summary>
public static class MyNotifications
{
    /// <summary>The longest pause a person can ask for with a duration: a week.</summary>
    public const int LongestPauseHours = 168;

    /// <summary>
    /// The most shows a person can turn off. Every send reads everyone's choices, so no list
    /// in them grows without end.
    /// </summary>
    public const int MostMutedShows = 1000;

    /// <summary>The most libraries a person can turn off, for the same reason.</summary>
    public const int MostMutedLibraries = 200;

    /// <summary>Describes a person's choices for the app.</summary>
    /// <param name="mine">What they chose, or nothing.</param>
    /// <param name="reaching">The events that can reach them.</param>
    /// <param name="libraries">The libraries they can open.</param>
    /// <param name="mutedShows">The shows they turned off.</param>
    /// <param name="showName">The name of a show, or <c>null</c> when the server no longer has it.</param>
    /// <returns>The description.</returns>
    public static MyNotificationsDto Describe(
        NotificationPreferences? mine,
        IEnumerable<string> reaching,
        IEnumerable<(Guid Id, string Name)> libraries,
        IEnumerable<Guid> mutedShows,
        Func<Guid, string?> showName)
    {
        mine ??= new NotificationPreferences();

        return new MyNotificationsDto
        {
            Pause = mine.Pause,
            Events = [.. reaching.Select(key => new MyEventDto
            {
                Key = key,
                Family = NotificationFamilies.Of(key),
                Enabled = mine.Keeps(key)
            })],
            Libraries = [.. libraries.Select(library => new MyLibraryDto
            {
                Id = library.Id,
                Name = library.Name,
                Enabled = !mine.MutedLibraries.Contains(library.Id)
            })],
            Follow = mine.Follow,
            MutedShows = [.. mutedShows.Select(id => new MyShowDto { Id = id, Name = showName(id) ?? id.ToString("N") })]
        };
    }

    /// <summary>The libraries whose new items can reach a person.</summary>
    /// <param name="folders">The server's libraries.</param>
    /// <param name="announced">The libraries the server announces, or none for all of them.</param>
    /// <param name="canOpen">Whether the person may open a library.</param>
    /// <returns>Each library by id and name, in the server's order.</returns>
    /// <remarks>
    /// A library the server never announces, or one the person cannot open, has nothing for
    /// them to turn off, so it is not offered.
    /// </remarks>
    public static IEnumerable<(Guid Id, string Name)> LibrariesFor(
        IEnumerable<VirtualFolderInfo> folders,
        string[]? announced,
        Func<Guid, bool> canOpen)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(canOpen);

        foreach (var folder in folders)
        {
            if (ItemAddedService.IsLibraryEnabled(announced, folder.ItemId)
                && Guid.TryParse(folder.ItemId, out var id)
                && canOpen(id))
            {
                yield return (id, folder.Name);
            }
        }
    }

    /// <summary>What is wrong with an update, if anything.</summary>
    /// <param name="update">The update.</param>
    /// <returns>A sentence for the app, or <c>null</c> when it can be stored.</returns>
    public static string? Problem(MyNotificationsUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        var unknown = update.Events?.Keys.FirstOrDefault(key => !NotificationEvents.All.Contains(key));
        if (unknown is not null)
        {
            return $"There is no notification called {unknown}.";
        }

        return ListsProblem(update.MutedShows?.Count ?? 0, update.MutedLibraries?.Count ?? 0);
    }

    /// <summary>What makes lists this long too long to keep, if anything.</summary>
    /// <param name="mutedShows">How many shows are turned off.</param>
    /// <param name="mutedLibraries">How many libraries are turned off.</param>
    /// <returns>A sentence for the app, or <c>null</c> when they can be kept.</returns>
    public static string? ListsProblem(int mutedShows, int mutedLibraries) =>
        mutedShows > MostMutedShows ? $"At most {MostMutedShows} shows can be turned off."
        : mutedLibraries > MostMutedLibraries ? $"At most {MostMutedLibraries} libraries can be turned off."
        : null;

    /// <summary>Turns a show off, once.</summary>
    /// <param name="mine">The person's choices, changed in place.</param>
    /// <param name="seriesId">The show.</param>
    /// <returns>A sentence for the app when they already turned off the most, or <c>null</c>.</returns>
    public static string? Mute(NotificationPreferences mine, Guid seriesId)
    {
        ArgumentNullException.ThrowIfNull(mine);

        if (mine.MutedShows.Contains(seriesId))
        {
            return null;
        }

        if (mine.MutedShows.Count >= MostMutedShows)
        {
            return $"At most {MostMutedShows} shows can be turned off.";
        }

        mine.MutedShows.Add(seriesId);
        return null;
    }

    /// <summary>The stored form of an update, with the defaults for what it leaves out.</summary>
    /// <param name="update">The update.</param>
    /// <returns>What to store.</returns>
    public static NotificationPreferences Apply(MyNotificationsUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        return new NotificationPreferences
        {
            Pause = update.Pause,
            Events = update.Events is null ? new(StringComparer.Ordinal) : new(update.Events, StringComparer.Ordinal),
            MutedLibraries = update.MutedLibraries ?? [],
            Follow = update.Follow ?? new(),
            MutedShows = update.MutedShows ?? []
        };
    }

    /// <summary>The pause a number of hours asks for.</summary>
    /// <param name="hours">The hours, or <c>null</c> until lifted.</param>
    /// <param name="nowUtc">The moment it is asked.</param>
    /// <returns>The pause, or <c>null</c> when the hours make no sense.</returns>
    public static NotificationPause? PauseFor(int? hours, DateTime nowUtc) => hours switch
    {
        null => new NotificationPause(),
        >= 1 and <= LongestPauseHours => new NotificationPause { Until = nowUtc.AddHours(hours.Value) },
        _ => null
    };
}
