using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Querying;
using NJsonSchema.Annotations;
using System.Xml.Serialization;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;


public class LibraryOptions
{
    public DisplayType display { get; set; } = DisplayType.list;
    public CardStyle cardStyle { get; set; } = CardStyle.detailed;
    public ImageStyle imageStyle { get; set; } = ImageStyle.cover;
    public bool showTitles { get; set; } = true;
    public bool showStats { get; set; } = true;
};

/// <summary>
/// Assign a lock to given type value 
/// </summary>
/// <typeparam name="T"></typeparam>
public class Lockable<T>
{
  public bool locked { get; set; } = false;
  public required T value { get; set; }
}


public class Home
{
  [NotNull]
  [Display(Name = "Sections")]
  // public SerializableDictionary<string, Section>? sections { get; set; }
  public Section[]? sections { get; set; }
}

public class Section
{  
  [NotNull]
  public string title { get; set; } = string.Empty;

  [NotNull]
  [Display(Name = "Media poster orientation")]
  public SectionOrientation? orientation { get; set; }

  [NotNull]
  [Display(Name = "Items", Description = "Customize the Items API query")]
  public Items? items { get; set; }
  
  [NotNull]
  [Display(Name = "Next up", Description = "Customize the Tv Shows Next Up API query")]
  public NextUp? nextUp { get; set; }

  [NotNull]
  [Display(Name = "Latest", Description = "Customize the Latest API query")]
  public Latest? latest { get; set; }
  
  [Display(Name = "Custom endpoint", Description = "Customize the Custom API query")]
  public CustomEndpoint? custom { get; set; }
}

public enum SectionOrientation
{
  vertical,
  horizontal
}

public enum SectionType
{
  row,
  carousel,
}

public class Items
{
  [Display(Name = "Sort by")]
  public ItemSortBy[]? sortBy { get; set; }
  
  [Display(Name = "Sort order")]
  public SortOrder[]? sortOrder { get; set; }
  
  [Display(Name = "Genres")]
  public Collection<string>? genres { get; set; }
  
  [Display(Name = "Parent id")]
  public string? parentId { get; set; }
  
  [Display(Name = "Filters")]
  public ItemFilter[]? filters { get; set; }
  
  [Display(Name = "Include item types")]
  public BaseItemKind[]? includeItemTypes { get; set; }
  
  [Display(Name = "Page limit")]
  public int? limit { get; set; }
}

public class NextUp
{
  [Display(Name = "Parent id")]
  public string? parentId { get; set; }
  
  [Display(Name = "Page limit")]
  public int? limit { get; set; }
  
  [Display(Name = "Enable resumable")]
  public bool? enableResumable { get; set; }
  
  [Display(Name = "Enable rewatching")]
  public bool? enableRewatching { get; set; }
}

public class Latest
{
  [Display(Name = "Parent id")]
  public string? parentId { get; set; }
  
  [Display(Name = "Page limit")]
  public int? limit { get; set; }
  
  [Display(Name = "Group items")]
  public bool? groupItems { get; set; }
    
  [Display(Name = "Is played")]
  public bool? isPlayed { get; set; }

  [Display(Name = "Include item types")]
  public BaseItemKind[]? includeItemTypes { get; set; }
  
}

public class CustomEndpoint
{
  [Display(Name = "Endpoint")]
  public string endpoint { get; set; } = string.Empty;
  
  [Display(Name = "Request headers")]
  public SerializableDictionary<string, string>? headers { get; set; }
  
  [Display(Name = "Query parameters")]
  public SerializableDictionary<string, string>? query { get; set; }
}

public class SectionSuggestions
{
  public SuggestionsArgs? args { get; set; }
}

public class SuggestionsArgs
{
  public BaseItemKind[]? type { get; set; }
}

/// <summary>
/// Streamyfin application settings
/// </summary>
public class Settings
{
    [NotNull]
    [Display(Name = "Home view", Description = "Customize the appearance of the apps home page")]
    public Lockable<Home>? home { get; set; }

    [NotNull]
    [Display(Name = "Show titles on the home screen", Description = "Show the title under each card on the home screen")]
    public Lockable<bool>? showHomeTitles { get; set; } // = true;

    [NotNull]
    [Display(Name = "Show the home backdrop", Description = "Show a backdrop image behind the home screen")]
    public Lockable<bool>? showHomeBackdrop { get; set; } // = true;

    [NotNull]
    [Display(Name = "Show the hero carousel", Description = "Show the large rotating carousel at the top of the home screen")]
    public Lockable<bool>? showHeroCarousel { get; set; } // = true;

    // string[] rather than an array of a declared enum, following hiddenLibraries. An
    // enum would validate the members, and would also make an administrator's YAML
    // fail to load the day the app adds a section name the plugin does not know yet.
    [NotNull]
    [Display(Name = "Hidden hero sections", Description = "Content groups to keep out of the hero carousel: continueWatching, nextUp, recentlyAdded")]
    public Lockable<string[]>? hiddenHomeHeroSections { get; set; } // = [];

    [NotNull]
    [Display(Name = "Hidden hero media types", Description = "Media kinds to keep out of the hero carousel: movie, tv")]
    public Lockable<string[]>? hiddenHomeHeroMediaTypes { get; set; } // = [];

    [NotNull]
    [Display(Name = "Merge Next Up and Continue Watching", Description = "Show both in a single row instead of two")]
    public Lockable<bool>? mergeNextUpAndContinueWatching { get; set; } // = false;

    [NotNull]
    [Display(Name = "Use episode images in Next Up", Description = "Show the episode's own image rather than the series poster")]
    public Lockable<bool>? useEpisodeImagesForNextUp { get; set; } // = false;

    [NotNull]
    [Display(Name = "Show the series poster on an episode", Description = "Use the series poster rather than the episode image on an episode page")]
    public Lockable<bool>? showSeriesPosterOnEpisode { get; set; } // = false;

    // Media Controls
    [NotNull]
    [Display(Name = "Forward skip time", Description = "The amount of time in seconds you want to be able to skip forward during playback")]
    public Lockable<int>? forwardSkipTime { get; set; } // = 30;
    
    [NotNull]
    [Display(Name = "Rewind skip time", Description = "The amount of time in seconds you want to be able to rewind during playback")]
    public Lockable<int>? rewindSkipTime { get; set; } // = 10;

    // Media segment skip preferences
    [NotNull]
    [Display(Name = "Skip intro", Description = "Automatically skip intros during playback: none, ask, or auto")]
    public Lockable<SegmentSkipMode>? skipIntro { get; set; } // = ask

    [NotNull]
    [Display(Name = "Skip outro", Description = "Automatically skip outros/credits during playback: none, ask, or auto")]
    public Lockable<SegmentSkipMode>? skipOutro { get; set; } // = ask

    [NotNull]
    [Display(Name = "Skip recap", Description = "Automatically skip recaps during playback: none, ask, or auto")]
    public Lockable<SegmentSkipMode>? skipRecap { get; set; } // = ask

    [NotNull]
    [Display(Name = "Skip commercial", Description = "Automatically skip commercials during playback: none, ask, or auto")]
    public Lockable<SegmentSkipMode>? skipCommercial { get; set; } // = ask

    [NotNull]
    [Display(Name = "Skip preview", Description = "Automatically skip previews during playback: none, ask, or auto")]
    public Lockable<SegmentSkipMode>? skipPreview { get; set; } // = ask
    
    // Audio
    [NotNull]
    [Display(Name = "Remember audio selection", Description = "Allows you to set the audio language from the previous played item")]
    public Lockable<bool>? rememberAudioSelections { get; set; } // = true;

    [NotNull]
    [Display(Name = "Prefer local audio", Description = "Prefer downloaded audio over streaming when it is available")]
    public Lockable<bool>? preferLocalAudio { get; set; } // = true;

    [NotNull]
    [Display(Name = "Audio look-ahead caching", Description = "Pre-cache upcoming tracks for gapless music playback")]
    public Lockable<bool>? audioLookaheadEnabled { get; set; } // = true;

    [NotNull]
    [Display(Name = "Audio look-ahead count", Description = "How many upcoming tracks to pre-cache")]
    public Lockable<int>? audioLookaheadCount { get; set; } // = 1;

    [NotNull]
    [Display(Name = "Audio max cache size (MB)", Description = "Maximum disk space used for audio look-ahead caching")]
    public Lockable<int>? audioMaxCacheSizeMB { get; set; } // = 500;
    // TODO create type converter for CultureDto
    //  Currently fails since it doesnt have a parameterless constructor
    // public Lockable<CultureDto?>? defaultAudioLanguage { get; set; }
    
    // Subtitles
    // public Lockable<CultureDto?>? defaultSubtitleLanguage { get; set; }
    [NotNull]
    [Display(Name = "Subtitle playback mode", Description = "Setting to determine when subtitles will automatically play during video playback")]
    public Lockable<SubtitlePlaybackMode>? subtitleMode { get; set; }

    [NotNull]
    [Display(Name = "Remember subtitle selection", Description = "Allows you to set the subtitle language from the previous played item")]
    public Lockable<bool>? rememberSubtitleSelections { get; set; } // = true;

    [NotNull]
    [Display(Name = "Subtitles when muted", Description = "Turn subtitles on automatically while the sound is off, and turn them back off when it returns")]
    public Lockable<bool>? subtitlesOnMute { get; set; } // = true;

    [NotNull]
    [Display(Name = "Allow restarting playback for subtitles when muted", Description = "Some subtitle formats cannot be turned on without the server re-processing the stream, which briefly interrupts playback")]
    public Lockable<bool>? subtitlesOnMuteAllowRestart { get; set; } // = false;

    [NotNull]
    [Display(Name = "Subtitle scale size", Description = "Adjust the subtitle size during video playback")]
    public Lockable<int>? subtitleSize { get; set; } // = 80;

    [NotNull]
    [Display(Name = "Subtitle font", Description = "Font family used to render subtitles")]
    public Lockable<string>? subtitleFont { get; set; } // = "System";

    [NotNull]
    [Display(Name = "Subtitle colour", Description = "Subtitle text colour, as a hex value such as #FFFFFF")]
    public Lockable<string>? subtitleColor { get; set; } // = "#FFFFFF";

    [NotNull]
    [Display(Name = "Subtitle background", Description = "Draw a box behind the subtitles")]
    public Lockable<bool>? subtitleBackground { get; set; } // = false;

    [NotNull]
    [Display(Name = "Subtitle background opacity", Description = "How opaque the subtitle background is, from 0 to 100")]
    public Lockable<int>? subtitleBackgroundOpacity { get; set; } // = 60;

    [NotNull]
    [Display(Name = "Subtitle background padding", Description = "Space between the subtitle text and the edge of its background")]
    public Lockable<int>? subtitleBackgroundPadding { get; set; } // = 8;

    [NotNull]
    [Display(Name = "Subtitle vertical margin", Description = "Distance between the subtitles and the edge of the video")]
    public Lockable<int>? subtitleMarginY { get; set; } // = 25;

    [NotNull]
    [Display(Name = "Subtitle horizontal alignment", Description = "left, center or right")]
    public Lockable<SubtitleAlignX>? subtitleAlignX { get; set; } // = center;

    [NotNull]
    [Display(Name = "Subtitle vertical alignment", Description = "top, center or bottom")]
    public Lockable<SubtitleAlignY>? subtitleAlignY { get; set; } // = bottom;

    // Other
    [NotNull]
    [Display(Name = "Default video orientation", Description = "Lock orientation during video playback")]
    public Lockable<OrientationLock>? defaultVideoOrientation { get; set; }
    
    [NotNull]
    [Display(Name = "Safe Area in video controls", Description = "Enable or disable the safe area for video controls")]
    public Lockable<bool>? safeAreaInControlsEnabled { get; set; } // = true;
    
    [NotNull]
    [Display(Name = "Show custom menu links", Description = "Show custom menu links in Jellyfin's web configuration")]
    public Lockable<bool>? showCustomMenuLinks { get; set; } // = false;
    
    [NotNull]
    [Display(Name = "Hidden libraries", Description = "Enter all library Ids you want hidden from users")]
    public Lockable<string[]>? hiddenLibraries { get; set; } // = [];

    [NotNull]
    [Display(Name = "Disable haptic feedback")]
    public Lockable<bool>? disableHapticFeedback { get; set; } // = false;

    [NotNull]
    [Display(Name = "Default playback quality")]
    public Lockable<Bitrate?>? defaultBitrate { get; set; } // = null/MAX;

    [NotNull]
    [Display(Name = "Max auto play episode count")]
    public Lockable<int>? maxAutoPlayEpisodeCount { get; set; } // = 3

    [NotNull]
    [Display(Name = "Auto play next episode", Description = "Automatically start the next episode when one finishes")]
    public Lockable<bool>? autoPlayNextEpisode { get; set; } // = true

    [NotNull]
    [Display(Name = "Default playback speed", Description = "The default video playback speed multiplier")]
    public Lockable<double>? defaultPlaybackSpeed { get; set; } // = 1.0

    // Swipe controls

    [NotNull]
    [Display(Name = "Horizontal swipe to skip")]
    public Lockable<bool>? enableHorizontalSwipeSkip { get; set; } // = true 
    
     [NotNull]
    [Display(Name = "Left side brightness control")]
    public Lockable<bool>? enableLeftSideBrightnessSwipe { get; set; } // = true

    [NotNull]
    [Display(Name = "Right side volume control")]
    public Lockable<bool>? enableRightSideVolumeSwipe { get; set; } // = true

    [NotNull]
    [Display(Name = "Hide volume slider", Description = "Hide the volume slider in the video controls")]
    public Lockable<bool>? hideVolumeSlider { get; set; } // = false

    [NotNull]
    [Display(Name = "Hide brightness slider", Description = "Hide the brightness slider in the video controls")]
    public Lockable<bool>? hideBrightnessSlider { get; set; } // = false

    [NotNull]
    [Display(Name = "Double tap to seek")]
    public Lockable<bool>? enableDoubleTapToSeek { get; set; } // = false;

    [NotNull]
    [Display(Name = "Hold to speed up")]
    public Lockable<bool>? enableHoldToSpeed { get; set; } // = true;

    [NotNull]
    [Display(Name = "Hold to speed rate", Description = "Playback speed multiplier while the screen is held")]
    public Lockable<double>? holdToSpeedRate { get; set; } // = 2.0;

    [NotNull]
    [Display(Name = "Pinch to zoom")]
    public Lockable<bool>? enablePinchToZoom { get; set; } // = true;

    [NotNull]
    [Display(Name = "Ask before resuming", Description = "Ask whether to resume or start over instead of resuming straight away")]
    public Lockable<bool>? showResumeDialog { get; set; } // = false;

    [NotNull]
    [Display(Name = "Auto play episode count", Description = "How many episodes have played automatically so far. 0 starts the count over")]
    public Lockable<int>? autoPlayEpisodeCount { get; set; } // = 0;

    [NotNull]
    [Display(Name = "Play the default audio track", Description = "Play the track the server marks as default rather than the last one chosen")]
    public Lockable<bool>? playDefaultAudioTrack { get; set; } // = true;

    [NotNull]
    [Display(Name = "Audio transcoding mode", Description = "How surround audio is handled: auto, stereo, 5.1 or passthrough")]
    public Lockable<AudioTranscodeMode>? audioTranscodeMode { get; set; } // = auto;

    // region Plugins
    // Jellyseerr
    [NotNull]
    [Display(Name = "Jellyseerr Server URL", Description = "Enter the url for your jellyseerr server. **Jellyfin authentication is required**")]
    public Lockable<string>? jellyseerrServerUrl { get; set; }

    [NotNull]
    [Display(Name = "Jellyseerr API Key", Description = "Seerr admin API key (Seerr Settings > General). Lets Streamyfin sign each user in to Seerr without a password. **Warning: every authenticated Jellyfin user on this server can read this key and it grants full admin access to the Seerr API — only set it if you trust all of your users.** Requires a Seerr version with the /user/jellyfin/{id} route.")]
    [Secret]
    public Lockable<string>? jellyseerrApiKey { get; set; }

    // Marlin Search
    [NotNull]
    [Display(Name = "Default search engine", Description = "Enter the search engine you want to use in streamyfin")]
    public Lockable<SearchEngine>? searchEngine { get; set; } // = SearchEngine.Jellyfin;
    
    [NotNull]
    [Display(Name = "Marlin server URL", Description = "Enter the URL for your Marlin server")]
    public Lockable<string>? marlinServerUrl { get; set; }

    // Streamystats
    [NotNull]
    [Display(Name = "Streamystats Server URL", Description = "Enter the URL for your Streamystats server")]
    public Lockable<string>? streamyStatsServerUrl { get; set; }
    
    [NotNull]
    [Display(Name = "Streamystats Movie Recommendations", Description = "Allow Streamystats to provide movie recommendations using your watch history")]
    public Lockable<bool>? streamyStatsMovieRecommendations { get; set; }
    
    [NotNull]
    [Display(Name = "Streamystats Series Recommendations", Description = "Allow Streamystats to provide series recommendations using your watch history")]
    public Lockable<bool>? streamyStatsSeriesRecommendations { get; set; }
    
    [NotNull]
    [Display(Name = "Streamystats Promoted Watchlists", Description = "Allow Streamystats to promote watchlists using your watch history")]
    public Lockable<bool>? streamyStatsPromotedWatchlists { get; set; }

    [NotNull]
    [Display(Name = "Hide watchlists tab", Description = "Hide the Streamystats watchlists tab in the app")]
    public Lockable<bool>? hideWatchlistsTab { get; set; }

    // KefinTweaks
    [NotNull]
    [Display(Name = "KefinTweaks watchlist integration", Description = "Enable the KefinTweaks watchlist integration")]
    public Lockable<bool>? useKefinTweaks { get; set; }

    [NotNull]
    [Display(Name = "Popular lists", Description = "Show popular lists from the Popular Lists plugin")]
    public Lockable<bool>? usePopularPlugin { get; set; } // = true;

    [NotNull]
    [Display(Name = "Awards from Wikidata", Description = "Show awards fetched from Wikidata on a title's page")]
    public Lockable<bool>? wikidataAwardsEnabled { get; set; } // = true;

    [NotNull]
    [Display(Name = "OpenSubtitles", Description = "Allow searching OpenSubtitles for subtitles during playback")]
    public Lockable<bool>? openSubtitlesEnabled { get; set; } // = true;

    [NotNull]
    [Display(Name = "Sign in to Jellyseerr automatically", Description = "Sign the user in to Jellyseerr without asking, when the server allows it")]
    public Lockable<bool>? autoLoginJellyseerr { get; set; } // = true;

    // No default. The app leaves this undefined and follows the device language until
    // the user picks one, so shipping a value would impose a language on everyone who
    // never chose.
    [NotNull]
    [Display(Name = "App language", Description = "Language code the app uses, such as fr or en")]
    public Lockable<string>? preferedLanguage { get; set; }

    [NotNull]
    [Display(Name = "Media list collections", Description = "Collection ids to offer as media lists in the app")]
    public Lockable<string[]>? mediaListCollectionIds { get; set; } // = [];

    [NotNull]
    [Display(Name = "Download live activity", Description = "Show download progress on the lock screen")]
    public Lockable<bool>? showDownloadLiveActivity { get; set; } // = true;

    [NotNull]
    [Display(Name = "HEVC for Chromecast", Description = "Offer HEVC to a Chromecast, which only some models decode")]
    public Lockable<bool>? enableH265ForChromecast { get; set; } // = false;
    // endregion Plugins
    
    // Misc.
    [NotNull]
    [Display(Name = "Library options", Description = "Customize how you want Streamyfin's library tab to look")]
    public Lockable<LibraryOptions>? libraryOptions { get; set; }

    // TV
    [NotNull]
    [Display(Name = "TV typography scale", Description = "Text size on the TV app: small, default, large or extraLarge")]
    public Lockable<TVTypographyScale>? tvTypographyScale { get; set; } // = default;

    [NotNull]
    [Display(Name = "TV theme music", Description = "Play a series theme music while browsing it on TV")]
    public Lockable<bool>? tvThemeMusicEnabled { get; set; } // = true;

    [NotNull]
    [Display(Name = "Hide the remote session button")]
    public Lockable<bool>? hideRemoteSessionButton { get; set; } // = false;

    [NotNull]
    [Display(Name = "Inactivity timeout", Description = "Sign out of the TV app after this long with no activity, in milliseconds. 0 never signs out")]
    public Lockable<InactivityTimeout>? inactivityTimeout { get; set; } // = Disabled;

    [NotNull]
    [Display(Name = "Native player on Apple TV")]
    public Lockable<bool>? nativeVideoPlayerTV { get; set; } // = true;

    [NotNull]
    [Display(Name = "Native player on Android TV")]
    public Lockable<bool>? nativeVideoPlayerAndroidTV { get; set; } // = false;

    // No default on purpose. The app resolves this at runtime through
    // getActiveVideoPlayer() so an existing install keeps the player it has been
    // using. A default here would choose for every device that never has.
    [NotNull]
    [Display(Name = "Video player", Description = "0 for mpv, 1 for ExoPlayer, which is Android TV only, 2 for the native player")]
    public Lockable<VideoPlayer>? videoPlayer { get; set; }

}

[XmlRoot("dictionary")]
public class SerializableDictionary<TKey, TValue>
       : Dictionary<TKey, TValue>, IXmlSerializable
  where TKey : notnull
{
  #region IXmlSerializable Members
  public System.Xml.Schema.XmlSchema? GetSchema()
  {
    return null;
  }

  public void ReadXml(System.Xml.XmlReader reader)
  {
    XmlSerializer keySerializer = new XmlSerializer(typeof(TKey));
    XmlSerializer valueSerializer = new XmlSerializer(typeof(TValue));

    bool wasEmpty = reader.IsEmptyElement;
    reader.Read();

    if (wasEmpty)
      return;

    while (reader.NodeType != System.Xml.XmlNodeType.EndElement)
    {
      reader.ReadStartElement("item");

      reader.ReadStartElement("key");
      var key = (TKey?)keySerializer.Deserialize(reader);
      reader.ReadEndElement();

      reader.ReadStartElement("value");
      var value = (TValue?)valueSerializer.Deserialize(reader);
      reader.ReadEndElement();

      // A pair the serializer could not read is skipped rather than added as a
      // null key, which Dictionary rejects at runtime anyway.
      if (key is not null && value is not null)
      {
        Add(key, value);
      }

      reader.ReadEndElement();
      reader.MoveToContent();
    }
    reader.ReadEndElement();
  }

  public void WriteXml(System.Xml.XmlWriter writer)
  {
    XmlSerializer keySerializer = new XmlSerializer(typeof(TKey));
    XmlSerializer valueSerializer = new XmlSerializer(typeof(TValue));

    foreach (TKey key in this.Keys)
    {
      writer.WriteStartElement("item");

      writer.WriteStartElement("key");
      keySerializer.Serialize(writer, key);
      writer.WriteEndElement();

      writer.WriteStartElement("value");
      TValue value = this[key];
      valueSerializer.Serialize(writer, value);
      writer.WriteEndElement();

      writer.WriteEndElement();
    }
  }
  #endregion
}
