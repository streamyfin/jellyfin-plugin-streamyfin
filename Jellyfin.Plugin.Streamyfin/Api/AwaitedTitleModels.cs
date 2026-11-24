using System;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Streamyfin.Db;

namespace Jellyfin.Plugin.Streamyfin.Api;

/// <summary>
/// A title the app asks to be told about, from a Seerr page (#225).
/// </summary>
public sealed class AwaitTitleRequest
{
    /// <summary>Gets or sets the kind of title, <c>movie</c> or <c>tv</c>.</summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    /// <summary>Gets or sets its TMDB id.</summary>
    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    /// <summary>Gets or sets its TVDB id, which Seerr gives for a show.</summary>
    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; }

    /// <summary>Gets or sets its name.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the year it came out.</summary>
    [JsonPropertyName("year")]
    public int? Year { get; set; }

    /// <summary>The row the caller waits on. Checked before, with <c>AwaitedTitles.Problem</c>.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="nowUtc">When they asked.</param>
    /// <returns>The row.</returns>
    public AwaitedTitle ToRow(Guid userId, DateTime nowUtc) => new()
    {
        UserId = userId,
        MediaType = MediaType ?? string.Empty,
        TmdbId = TmdbId,
        TvdbId = TvdbId,
        Title = (Title ?? string.Empty).Trim(),
        Year = Year,
        AddedAt = nowUtc
    };
}

/// <summary>
/// A title the caller waits for, as the app lists it.
/// </summary>
public sealed class AwaitedTitleDto
{
    /// <summary>Gets or sets the kind of title.</summary>
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    /// <summary>Gets or sets its TMDB id.</summary>
    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    /// <summary>Gets or sets its TVDB id.</summary>
    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; }

    /// <summary>Gets or sets its name.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the year it came out.</summary>
    [JsonPropertyName("year")]
    public int? Year { get; set; }

    /// <summary>Gets or sets when the caller asked, in UTC.</summary>
    [JsonPropertyName("addedAt")]
    public DateTime AddedAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether it arrived while the caller had paused their
    /// notifications, and is told once the pause is over.
    /// </summary>
    [JsonPropertyName("arrived")]
    public bool Arrived { get; set; }

    /// <summary>Describes a row for the app.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The description.</returns>
    public static AwaitedTitleDto From(AwaitedTitle row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new AwaitedTitleDto
        {
            MediaType = row.MediaType,
            TmdbId = row.TmdbId,
            TvdbId = row.TvdbId,
            Title = row.Title,
            Year = row.Year,
            AddedAt = row.AddedAt,
            Arrived = row.ArrivedItemId is not null
        };
    }
}
