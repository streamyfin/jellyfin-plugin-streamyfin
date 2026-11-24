using System.Collections.Generic;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// The groups the app's hero carousel draws from, as its own filter names them.
/// </summary>
/// <remarks>
/// <c>SECTION_LABEL_KEYS</c> in the app's <c>HomeHeroCarousel.tsx</c>, under the English of
/// each label. <c>SettingsParityTests</c> holds this to the list the manifest records.
/// </remarks>
public static class HomeHeroSections
{
    /// <summary>
    /// Gets the groups, in the order the app's filter lists them.
    /// </summary>
    public static IReadOnlyList<SettingsChoice> Choices { get; } =
    [
        new("continueWatching", "Continue watching"),
        new("nextUp", "Next up"),
        new("recentlyAdded", "Recently added"),
    ];
}

/// <summary>
/// The kinds of media the app's hero carousel shows, as its own filter names them.
/// </summary>
/// <remarks>
/// <c>MEDIA_LABEL_KEYS</c> in the app's <c>HomeHeroCarousel.tsx</c>. Movies and series are
/// all the carousel draws, so there is nothing else to keep out of it.
/// </remarks>
public static class HomeHeroMediaTypes
{
    /// <summary>
    /// Gets the kinds, in the order the app's filter lists them.
    /// </summary>
    public static IReadOnlyList<SettingsChoice> Choices { get; } =
    [
        new("movie", "Movies"),
        new("tv", "TV shows"),
    ];
}
