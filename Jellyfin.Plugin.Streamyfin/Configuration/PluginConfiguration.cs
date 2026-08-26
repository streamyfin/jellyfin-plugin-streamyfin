#pragma warning disable CA2227
#pragma warning disable CS0219

using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.Streamyfin.Configuration;


/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
  public Config Config { get; set; }

  public PluginConfiguration()
  {
    Config = DefaultConfig();
  }

  public static Config DefaultConfig() => new()
  {
    notifications = DefaultNotifications(),
    settings = DefaultSettings()
  };

  public static Notifications.Notifications DefaultNotifications() => new()
  {
    SessionStarted = new()
    {
      Enabled = true
    },
    PlaybackStarted = new()
    {
      Enabled = true
    },
    UserLockedOut = new ()
    {
      Enabled = true
    },
    ItemAdded = new()
    {
      Enabled = true,
      EnabledLibraries = []
    }
  };

  public static Settings.Settings DefaultSettings() => new()
  {
    forwardSkipTime = new() { value = 30 },
    rewindSkipTime = new() { value = 10 },
    rememberAudioSelections = new() { value = true },
    subtitleMode = new() { value = SubtitlePlaybackMode.Default },
    rememberSubtitleSelections = new() { value = true },
    subtitlesOnMute = new() { value = true },
    subtitlesOnMuteAllowRestart = new() { value = false },
    subtitleSize = new() { value = 100 },
    subtitleFont = new() { value = "System" },
    subtitleColor = new() { value = "#FFFFFF" },
    subtitleBackground = new() { value = false },
    subtitleBackgroundOpacity = new() { value = 60 },
    subtitleBackgroundPadding = new() { value = 8 },
    subtitleMarginY = new() { value = 25 },
    subtitleAlignX = new() { value = SubtitleAlignX.Center },
    subtitleAlignY = new() { value = SubtitleAlignY.Bottom },
    defaultVideoOrientation = new() { value = OrientationLock.Default },
    safeAreaInControlsEnabled = new() { value = true },
    showCustomMenuLinks = new() { value = false },
    showHomeTitles = new() { value = true },
    showHomeBackdrop = new() { value = true },
    showHeroCarousel = new() { value = true },
    hiddenHomeHeroSections = new() { value = [] },
    hiddenHomeHeroMediaTypes = new() { value = [] },
    mergeNextUpAndContinueWatching = new() { value = false },
    useEpisodeImagesForNextUp = new() { value = false },
    showSeriesPosterOnEpisode = new() { value = false },
    usePopularPlugin = new() { value = true },
    wikidataAwardsEnabled = new() { value = true },
    openSubtitlesEnabled = new() { value = true },
    autoLoginJellyseerr = new() { value = true },
    mediaListCollectionIds = new() { value = [] },
    showDownloadLiveActivity = new() { value = true },
    enableH265ForChromecast = new() { value = false },
    hiddenLibraries = new() { value = [] },
    disableHapticFeedback = new() { value = false },
    enableDoubleTapToSeek = new() { value = false },
    enableHoldToSpeed = new() { value = true },
    holdToSpeedRate = new() { value = 2.0 },
    enablePinchToZoom = new() { value = true },
    showResumeDialog = new() { value = false },
    autoPlayEpisodeCount = new() { value = 0 },
    playDefaultAudioTrack = new() { value = true },
    audioTranscodeMode = new() { value = AudioTranscodeMode.Auto },
    defaultBitrate = new() { value = null },
    jellyseerrServerUrl = new() { value = "" },
    searchEngine = new() { value = SearchEngine.Jellyfin },
    marlinServerUrl = new() { value = "" },
    libraryOptions = new() { value = new LibraryOptions() },
    tvTypographyScale = new() { value = TVTypographyScale.Default },
    tvThemeMusicEnabled = new() { value = true },
    hideRemoteSessionButton = new() { value = false },
    inactivityTimeout = new() { value = InactivityTimeout.Disabled },
    nativeVideoPlayerTV = new() { value = true },
    nativeVideoPlayerAndroidTV = new() { value = false },
    home = new()
    {
      value = new Home
      {
        sections = new Section[] {
          new() {
              title = "Continue Watching",
              orientation = SectionOrientation.vertical,
              items = new()
              {
                filters = [ItemFilter.IsResumable],
                includeItemTypes = [BaseItemKind.Episode, BaseItemKind.Movie],
                limit = 25,
              }
          },
            new() {
            title = "Nextup",
            orientation = SectionOrientation.horizontal,
            nextUp = new()
              {
                limit = 25,
              }
          },
          new() {
              title = "Recently Added",
              orientation = SectionOrientation.vertical,
              items = new()
              {
                sortBy = [ItemSortBy.DateCreated],
                sortOrder = [SortOrder.Descending],
                includeItemTypes = [BaseItemKind.Series, BaseItemKind.Movie],
                limit = 25,
              }
          },
          new() {
            title = "Latest",
            orientation = SectionOrientation.horizontal,
            latest = new()
              {
                limit = 25,
              }
          },
          new() {
              title = "Favorites",
              orientation = SectionOrientation.vertical,
              items = new()
              {
                sortBy = [ItemSortBy.Default],
                sortOrder = [SortOrder.Ascending],
                filters = [ItemFilter.IsFavorite, ItemFilter.IsUnplayed],
                includeItemTypes = [BaseItemKind.Series, BaseItemKind.Movie],
                limit = 25,
              }
          },
        }
      }
    },
  };
}
