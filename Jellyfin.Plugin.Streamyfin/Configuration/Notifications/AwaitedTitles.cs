using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Notifications;

/// <summary>
/// What a title a person waits for comes to when it arrives (#225).
/// </summary>
public enum AwaitedOutcome
{
    /// <summary>Tell them now, and stop waiting.</summary>
    Announce,

    /// <summary>They paused their notifications: keep it as arrived, and tell them once the pause is over.</summary>
    Hold,

    /// <summary>They cannot open what arrived: keep waiting for a copy they can.</summary>
    KeepWaiting,

    /// <summary>Nothing would tell them, the person is gone or the event does not reach them: stop waiting.</summary>
    Drop
}

/// <summary>
/// What adding a title to a person's list did.
/// </summary>
public enum AwaitResult
{
    /// <summary>It is on the list now.</summary>
    Added,

    /// <summary>It was on the list already.</summary>
    AlreadyAwaited,

    /// <summary>The list holds as many titles as a person may wait for.</summary>
    Full
}

/// <summary>
/// The rules of the titles people wait for (#225).
/// </summary>
/// <remarks>
/// Kept apart from Jellyfin's types, so what an arrival comes to is decided by four answers
/// a test can give.
/// </remarks>
public static class AwaitedTitles
{
    /// <summary>A movie, as Seerr says it.</summary>
    public const string Movie = "movie";

    /// <summary>A show, as Seerr says it.</summary>
    public const string Tv = "tv";

    /// <summary>
    /// The most titles one person may wait for. Every arrival reads the rows that match it, so
    /// no list grows without end.
    /// </summary>
    public const int MostAwaited = 1000;

    /// <summary>The longest name a title may have.</summary>
    public const int LongestTitle = 300;

    /// <summary>Whether this is a kind of title a person can wait for.</summary>
    /// <param name="mediaType">The kind, as Seerr says it.</param>
    /// <returns><c>true</c> for a movie or a show.</returns>
    public static bool IsMediaType(string? mediaType) => mediaType is Movie or Tv;

    /// <summary>What makes a title impossible to wait for.</summary>
    /// <param name="mediaType">The kind, as Seerr says it.</param>
    /// <param name="tmdbId">Its TMDB id.</param>
    /// <param name="tvdbId">Its TVDB id, when Seerr gave one.</param>
    /// <param name="title">Its name.</param>
    /// <returns>A sentence for the app, or <c>null</c> when it can be kept.</returns>
    public static string? Problem(string? mediaType, int tmdbId, int? tvdbId, string? title)
    {
        if (!IsMediaType(mediaType))
        {
            return "Say whether the title is a movie or a tv show, as Seerr does.";
        }

        if (tmdbId <= 0)
        {
            return "A title needs its TMDB id.";
        }

        if (tvdbId is <= 0)
        {
            return "A TVDB id is a positive number.";
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return "A title needs a name to be listed by.";
        }

        return title.Length > LongestTitle ? $"A title is at most {LongestTitle} characters." : null;
    }

    /// <summary>What an arrival comes to for one person who waited for it.</summary>
    /// <param name="personExists">Whether the account is there and not disabled.</param>
    /// <param name="reaches">Whether the event reaches them, by the levels and by their own choice.</param>
    /// <param name="canOpen">Whether they may open what arrived.</param>
    /// <param name="paused">Whether they paused their notifications.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// New items, a library or a show turned off do not count: this is a title the person asked
    /// for by name. A pause does, and holds the news back rather than losing it.
    /// </remarks>
    public static AwaitedOutcome Decide(bool personExists, bool reaches, bool canOpen, bool paused) =>
        !personExists || !reaches ? AwaitedOutcome.Drop
        : !canOpen ? AwaitedOutcome.KeepWaiting
        : paused ? AwaitedOutcome.Hold
        : AwaitedOutcome.Announce;

    /// <summary>The TMDB and TVDB ids an item carries.</summary>
    /// <param name="providerIds">The item's provider ids, which may be missing.</param>
    /// <returns>Each id, or <c>null</c> when it is missing or not a positive number.</returns>
    public static (int? Tmdb, int? Tvdb) IdsOf(IReadOnlyDictionary<string, string>? providerIds) =>
        (Positive(providerIds, "Tmdb"), Positive(providerIds, "Tvdb"));

    private static int? Positive(IReadOnlyDictionary<string, string>? ids, string provider) =>
        ids?.FirstOrDefault(pair => string.Equals(pair.Key, provider, StringComparison.OrdinalIgnoreCase)).Value is { } raw
        && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
        && id > 0
            ? id
            : null;
}
