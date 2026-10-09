using System;

namespace Jellyfin.Plugin.Streamyfin.Db;

/// <summary>
/// A title one person waits for, until it arrives on the server (#225).
/// </summary>
public class AwaitedTitle
{
    /// <summary>Gets or sets the Jellyfin user.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the kind of title, <c>movie</c> or <c>tv</c>, as Seerr says it.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>Gets or sets the TMDB id, which Seerr and Jellyfin share.</summary>
    public int TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the TVDB id of a show, when Seerr knows it: many shows in Jellyfin only
    /// carry that one.
    /// </summary>
    public int? TvdbId { get; set; }

    /// <summary>Gets or sets the title, to list it without asking Seerr.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the year it came out, when known.</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets when it was added, in UTC.</summary>
    public DateTime AddedAt { get; set; }

    /// <summary>
    /// Gets or sets the item that arrived while the person had paused their notifications,
    /// waiting to be announced, or <c>null</c> while the title has not arrived.
    /// </summary>
    public Guid? ArrivedItemId { get; set; }
}
