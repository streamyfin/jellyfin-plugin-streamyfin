using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Notifications;

/// <summary>
/// What one person keeps of the notifications their levels send them (P4.5).
/// </summary>
/// <remarks>
/// It only ever narrows: nothing here reaches somebody the server, their groups or an
/// administrator did not already send the event to. Everything left out means kept, so an
/// event, a library or a show added later arrives switched on.
/// </remarks>
public sealed class NotificationPreferences
{
    private static readonly JsonSerializerOptions _stored = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Gets or sets the pause, or <c>null</c> when nothing is paused.
    /// </summary>
    [JsonPropertyName("pause")]
    public NotificationPause? Pause { get; set; }

    /// <summary>
    /// Gets or sets each event this person turned on or off, by the key the levels use.
    /// </summary>
    [JsonPropertyName("events")]
    public Dictionary<string, bool> Events { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the libraries whose new items this person does not want announced.
    /// </summary>
    [JsonPropertyName("mutedLibraries")]
    public List<Guid> MutedLibraries { get; set; } = [];

    /// <summary>
    /// Gets or sets which shows count as followed.
    /// </summary>
    [JsonPropertyName("follow")]
    public FollowChoice Follow { get; set; } = new();

    /// <summary>
    /// Gets or sets the shows this person turned off, followed or not.
    /// </summary>
    [JsonPropertyName("mutedShows")]
    public List<Guid> MutedShows { get; set; } = [];

    /// <summary>
    /// Whether everything is paused at this moment.
    /// </summary>
    /// <param name="nowUtc">The moment asked about, in UTC.</param>
    /// <returns>True while a pause runs; a pause with no end runs until it is lifted.</returns>
    public bool IsPaused(DateTime nowUtc) =>
        Pause is { } pause && (pause.Until is not { } until || until > nowUtc);

    /// <summary>
    /// Whether this person keeps an event.
    /// </summary>
    /// <param name="eventKey">The event, by the key the levels use.</param>
    /// <returns>True unless they turned it off.</returns>
    public bool Keeps(string eventKey) => !Events.TryGetValue(eventKey, out var on) || on;

    /// <summary>
    /// Reads what is stored.
    /// </summary>
    /// <param name="json">The stored JSON, which nothing validated on its way in.</param>
    /// <returns>The preferences, or <c>null</c> when there are none or they cannot be read.</returns>
    /// <remarks>
    /// Tolerant on purpose, like the levels: a row broken by hand costs that person their
    /// choices, never a notification to the rest of the server.
    /// </remarks>
    public static NotificationPreferences? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var read = JsonSerializer.Deserialize<NotificationPreferences>(json);
            if (read is null)
            {
                return null;
            }

            // A list written as null by hand reads as an empty one rather than a crash later.
            read.Events = read.Events is null
                ? new(StringComparer.Ordinal)
                : new(read.Events, StringComparer.Ordinal);
            read.MutedLibraries ??= [];
            read.MutedShows ??= [];
            read.Follow ??= new();
            return read;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the preferences for storage.
    /// </summary>
    /// <param name="preferences">What to store.</param>
    /// <returns>The JSON.</returns>
    public static string Write(NotificationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return JsonSerializer.Serialize(preferences, _stored);
    }
}

/// <summary>
/// A pause of every notification.
/// </summary>
public sealed class NotificationPause
{
    /// <summary>
    /// Gets or sets when the pause ends, in UTC, or <c>null</c> until the person lifts it.
    /// </summary>
    [JsonPropertyName("until")]
    public DateTime? Until { get; set; }
}

/// <summary>
/// Which shows count as followed: a new episode of one reaches the person even with new
/// items turned off.
/// </summary>
public sealed class FollowChoice
{
    /// <summary>
    /// Gets or sets whether a show marked as favorite counts.
    /// </summary>
    [JsonPropertyName("favorites")]
    public bool Favorites { get; set; } = true;

    /// <summary>
    /// Gets or sets whether a show with at least one episode watched counts.
    /// </summary>
    [JsonPropertyName("started")]
    public bool Started { get; set; } = true;
}
