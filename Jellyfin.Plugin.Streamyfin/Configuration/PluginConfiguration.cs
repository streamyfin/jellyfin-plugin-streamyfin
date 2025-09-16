#pragma warning disable CA2227
#pragma warning disable CS0219

using System.Collections.Generic;
using Jellyfin.Data.Entities.Libraries;
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
  //public string Yaml { get; set; }
  public Config Config { get; set; }
  private readonly SerializationHelper _serializationHelper;

  public PluginConfiguration(
    SerializationHelper serializationHelper
  )
  {
    _serializationHelper = serializationHelper;
  }


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
      Enabled = true
    }
  };

  public static Settings.Settings DefaultSettings() => new()
  {
    forwardSkipTime = new() { value = 30 },
    rewindSkipTime = new() { value = 15 },
    rememberAudioSelections = new() { value = false },
    subtitleMode = new() { value = SubtitlePlaybackMode.Default },
    rememberSubtitleSelections = new() { value = false },
    subtitleSize = new() { value = 80 },
    autoRotate = new() { value = true },
    defaultVideoOrientation = new() { value = OrientationLock.Default },
    safeAreaInControlsEnabled = new() { value = true },
    showCustomMenuLinks = new() { value = false },
    hiddenLibraries = new() { value = new[] { "Enter library id(s)" } },
    disableHapticFeedback = new() { value = false },
    defaultBitrate = new() { value = null },
    downloadMethod = new() { value = DownloadMethod.remux },
    remuxConcurrentLimit = new() { value = RemuxConcurrentLimit.One },
    autoDownload = new() { value = false },
    optimizedVersionsServerUrl = new() { value = "Enter optimized server url" },
    jellyseerrServerUrl = new() { value = "Enter jellyseerr server url" },
    searchEngine = new() { value = SearchEngine.Jellyfin },
    marlinServerUrl = new() { value = "Enter marlin server url" },
  libraryOptions = new() { value = new LibraryOptions() },
  tvSidebarLinks = new() { value = new SidebarLink[] { } },
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
