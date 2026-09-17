using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Notifications;

/// <summary>
/// What a group or a user says about one event.
/// </summary>
/// <remarks>
/// Every field is optional, and one that is not set says nothing: the level below decides.
/// That is what lets a group turn an event on without also fixing how long two of them
/// wait.
/// </remarks>
public class NotificationTargeting
{
    /// <summary>
    /// Gets or sets whether this event reaches these people, or <c>null</c> to leave the
    /// level below to decide.
    /// </summary>
    [JsonPropertyName(name: "enabled")]
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets how long two of the same event wait, in seconds, or <c>null</c> to
    /// leave the level below to decide.
    /// </summary>
    [JsonPropertyName(name: "recentEventThreshold")]
    public double? RecentEventThreshold { get; set; }

    /// <summary>
    /// Whether an event reaches someone.
    /// </summary>
    /// <param name="eventKey">The event, by the key the configuration and the page use.</param>
    /// <param name="serverEnabled">Whether the server has the event on at all.</param>
    /// <param name="inDefaultAudience">
    /// Whether this person is who the event is for when nobody has said otherwise: an
    /// administrator for the events about the server, anybody for a new item.
    /// </param>
    /// <param name="levels">
    /// What each level says, least specific first: the groups in their order, then the
    /// person's own. A level that says nothing about this event leaves the one before it
    /// standing.
    /// </param>
    /// <returns>Whether to send it to them.</returns>
    public static bool Reaches(
        string eventKey,
        bool serverEnabled,
        bool inDefaultAudience,
        IEnumerable<IReadOnlyDictionary<string, NotificationTargeting>?> levels)
    {
        var reaches = serverEnabled && inDefaultAudience;

        foreach (var said in Said(eventKey, levels))
        {
            if (said.Enabled is { } enabled)
            {
                reaches = enabled;
            }
        }

        return reaches;
    }

    /// <summary>
    /// How long two of the same event wait for someone.
    /// </summary>
    /// <param name="eventKey">The event, by the key the configuration and the page use.</param>
    /// <param name="serverWait">What the server says, which may be nothing.</param>
    /// <param name="levels">What each level says, least specific first.</param>
    /// <returns>The wait in seconds, or <c>null</c> when nobody said.</returns>
    public static double? WaitOf(
        string eventKey,
        double? serverWait,
        IEnumerable<IReadOnlyDictionary<string, NotificationTargeting>?> levels)
    {
        var wait = serverWait;

        foreach (var said in Said(eventKey, levels))
        {
            if (said.RecentEventThreshold is { } seconds)
            {
                wait = seconds;
            }
        }

        return wait;
    }

    /// <summary>
    /// What a level says, read from what is stored.
    /// </summary>
    /// <param name="json">The stored JSON, which nothing validates on the way in.</param>
    /// <returns>What it says, or <c>null</c> when it says nothing or cannot be read.</returns>
    /// <remarks>
    /// Tolerant on purpose, the way the settings levels are: a row edited outside the
    /// plugin, or a partial write, must cost that level rather than every notification the
    /// server sends.
    /// </remarks>
    public static IReadOnlyDictionary<string, NotificationTargeting>? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var said = JsonSerializer.Deserialize<Dictionary<string, NotificationTargeting>>(json);

            return said is { Count: > 0 } ? said : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<NotificationTargeting> Said(
        string eventKey,
        IEnumerable<IReadOnlyDictionary<string, NotificationTargeting>?> levels)
    {
        foreach (var level in levels)
        {
            if (level is not null && level.TryGetValue(eventKey, out var said) && said is not null)
            {
                yield return said;
            }
        }
    }
}
