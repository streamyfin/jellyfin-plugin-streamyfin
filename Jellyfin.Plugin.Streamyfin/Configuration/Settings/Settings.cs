using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Querying;
using NJsonSchema.Annotations;
using System.Xml.Serialization;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

public class DownloadOption
{
    public required string label { get; set; }
    public required DownloadQuality value { get; set; }
};

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
  public SerializableDictionary<string, Section>? sections { get; set; }
}

public class Section
{
  [NotNull]
  [Display(Name = "Media poster orientation")]
  public SectionOrientation? orientation { get; set; }
  
  [NotNull]
  [Display(Name = "Items", Description = "Customize the Items API query")]
  public Items? items { get; set; }
  
  [NotNull]
  [Display(Name = "Next up", Description = "Customize the Tv Shows Next Up API query")]
  public NextUp? nextUp { get; set; }
//   public SectionSuggestions? suggestions { get; set; } = null;
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
  public List<string>? genres { get; set; }
  
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

    // Media Controls
    [NotNull]
    [Display(Name = "Forward skip time", Description = "The amount of time in seconds you want to be able to skip forward during playback")]
    public Lockable<int>? forwardSkipTime { get; set; } // = 30;
    
    [NotNull]
    [Display(Name = "Rewind skip time", Description = "The amount of time in seconds you want to be able to rewind during playback")]
    public Lockable<int>? rewindSkipTime { get; set; } // = 10;
    
    // Audio
    [NotNull]
    [Display(Name = "Remember audio selection", Description = "Allows you to set the audio language from the previous played item")]
    public Lockable<bool>? rememberAudioSelections { get; set; } // = true;
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
    [Display(Name = "Subtitle scale size", Description = "Adjust the subtitle size during video playback")]
    public Lockable<int>? subtitleSize { get; set; } // = 80;

    // Other
    [NotNull]
    [Display(Name = "Auto rotate", Description = "Grant ability to auto rotate during video playback")]
    public Lockable<bool>? autoRotate { get; set; } // true
    
    [NotNull]
    [Display(Name = "Default video orientation", Description = "Lock orientation during video playback")]
    public Lockable<OrientationLock>? defaultVideoOrientation { get; set; }
    
    [NotNull]
    [Display(Name = "Safe Area in video controls", Description = "Enable or disable the safe area for video controls")]
    public Lockable<bool>? safeAreaInControlsEnabled { get; set; } // = true;
    
    [NotNull]
    [Display(Name = "Show custom menu links", Description = "Show custom menu links in jellyfins web configuration")]
    public Lockable<bool>? showCustomMenuLinks { get; set; } // = false;
    
    [NotNull]
    [Display(Name = "Hidden libraries", Description = "Enter all library Ids you want hidden from users")]
    public Lockable<string[]>? hiddenLibraries { get; set; } // = [];

    [NotNull]
    [Display(Name = "Disable haptic feedback")]
    public Lockable<bool>? disableHapticFeedback { get; set; } // = false;

    // Downloads
    [NotNull]
    [Display(Name = "Offline download method", Description = "Enter the method you want your users to use when download media for offline usage")]
    public Lockable<DownloadMethod>? downloadMethod { get; set; }
    
    [NotNull]
    [Display(Name = "Remux concurrent limit", Description = "Restrict the amount of downloads a device can do simultaneously")]
    public Lockable<RemuxConcurrentLimit>? remuxConcurrentLimit { get; set; }

    [NotNull]
    [Display(Name = "Optimized auto download", Description = "Grant the ability to auto download in the background when using the optimized server.")]
    public Lockable<bool>? autoDownload { get; set; } // = false;

    [NotNull]
    [Display(Name = "Optimized server url", Description = "Enter the url for your optimized server.")]
    public Lockable<string>? optimizedVersionsServerUrl { get; set; }
    
    // region Plugins
    // Jellyseerr
    [NotNull]
    [Display(Name = "Jellyseerr Server URL", Description = "Enter the url for your jellyseerr server. **Jellyfin authentication is required**")]
    public Lockable<string>? jellyseerrServerUrl { get; set; }

    // Marlin Search
    [NotNull]
    [Display(Name = "Default search engine", Description = "Enter the search engine you want to use in streamyfin")]
    public Lockable<SearchEngine>? searchEngine { get; set; } // = SearchEngine.Jellyfin;
    
    [NotNull]
    [Display(Name = "Marlin server URL", Description = "Enter  url for your marlin server")]
    public Lockable<string>? marlinServerUrl { get; set; }

    // Popular Lists
    [NotNull]
    [Display(Name = "Enable popular plugin")]
    public Lockable<bool>? usePopularPlugin { get; set; } // = false;

    [NotNull]
    [Display(Name = "Popular plugin enabled collections", Description = "Enter the Ids of the collections you want this plugin to be enabled for")]
    public Lockable<string[]>? mediaListCollectionIds { get; set; } // = false;
    // endregion Plugins
    
    // Misc.
    [NotNull]
    [Display(Name = "Library options", Description = "Customize how you want streamfins library tab to look")]
    public Lockable<LibraryOptions>? libraryOptions { get; set; }
    
    // TODO: These are used outside of settings. Review usages/delete any unused later.
    // public Lockable<bool?>? forceLandscapeInVideoPlayer { get; set; }
    // public Lockable<DeviceProfile?>? deviceProfile { get; set; } // = DeviceProfile.Expo;
    // public Lockable<string[]?>? deviceProfile { get; set; } // = [];
    // public Lockable<bool?>? openInVLC { get; set; }
    // public Lockable<DownloadOption?>? downloadQuality { get; set; }
    // public Lockable<bool?>? playDefaultAudioTrack { get; set; } // = true;
    // public Lockable<bool?>? showHomeTitles { get; set; } // = true;
}

[XmlRoot("dictionary")]
public class SerializableDictionary<TKey, TValue>
       : Dictionary<TKey, TValue>, IXmlSerializable
{
  #region IXmlSerializable Members
  public System.Xml.Schema.XmlSchema GetSchema()
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
      TKey key = (TKey)keySerializer.Deserialize(reader);
      reader.ReadEndElement();

      reader.ReadStartElement("value");
      TValue value = (TValue)valueSerializer.Deserialize(reader);
      reader.ReadEndElement();

      this.Add(key, value);

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