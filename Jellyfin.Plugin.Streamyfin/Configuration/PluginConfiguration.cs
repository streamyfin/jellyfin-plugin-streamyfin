#pragma warning disable CA2227
#pragma warning disable CS0219

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
    settings = DefaultSettings()
  };

  public static Settings.Settings DefaultSettings() => new()
  {
    forwardSkipTime = new() { value = 30 },
    rewindSkipTime = new() { value = 15 },
    rememberAudioSelections = new() { value = false },
    subtitleMode = new() { value = SubtitlePlaybackMode.Default },
    rememberSubtitleSelections = new() { value = false },
    subtitleSize = new() { value = 80 },
    autoRotate = new () { value = true },
    defaultVideoOrientation = new () { value = OrientationLock.Default },
    safeAreaInControlsEnabled = new () { value = true },
    showCustomMenuLinks = new () { value = false },
    hiddenLibraries = new () { value = new[] { "Enter library id(s)" } },
    disableHapticFeedback = new () { value = false },
    downloadMethod = new () { value = DownloadMethod.remux },
    remuxConcurrentLimit = new () { value = RemuxConcurrentLimit.One },
    autoDownload = new () { value = false },
    optimizedVersionsServerUrl = new () { value = "Enter optimized server url" },
    jellyseerrServerUrl = new () { value = "Enter jellyseerr server url" },
    searchEngine = new () { value = SearchEngine.Jellyfin },
    marlinServerUrl = new() { value = "Enter marlin server url" },
    usePopularPlugin = new() { value = false },
    mediaListCollectionIds = new() { value = new [] { "Enter collection id(s)" } },
    libraryOptions = new() { value = new LibraryOptions() },
    home = new()
    {
      value = new Home
      {
        sections = new SerializableDictionary<string, Section>
        {
          {
            "Items Example",
            new Section
            {
              orientation = SectionOrientation.vertical,
              items = new()
              {
                parentId = "YOURCOLLECTIONID",
                sortBy = [ItemSortBy.Default],
                sortOrder = [SortOrder.Ascending],
                genres = ["Your genres"],
                filters = [ItemFilter.IsFavorite],
                includeItemTypes = [BaseItemKind.Episode, BaseItemKind.Movie],
                limit = 25,
              }
            }
          },
          {
            "Next Up Example",
            new Section
            {
              orientation = SectionOrientation.vertical,
              nextUp = new()
              {
                parentId = "YOURCOLLECTIONID",
                limit = 25,
                enableResumable = true,
                enableRewatching = true,
              }
            }
          },
        }
      }
    },
  };
}
