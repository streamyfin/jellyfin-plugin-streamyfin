using System.Collections.Generic;
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
    public required bool locked { get; set; }
    public required T value { get; set; }
}


public class Home
{
  [NotNull]
  public SerializableDictionary<string, Section>? sections { get; set; }
}

public class Section
{
  [NotNull]
  public SectionOrientation? orientation { get; set; }
  [NotNull]
  public ItemArgs? items { get; set; }
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

public class ItemArgs
{
  public ItemSortBy[]? sortBy { get; set; }
  public SortOrder[]? sortOrder { get; set; }
  public List<string>? genres { get; set; }
  public string? parentId { get; set; }
  public ItemFilter[]? filters { get; set; }
  public BaseItemKind[]? includeItemTypes { get; set; }
  public int? limit { get; set; }
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
    public Lockable<Home>? home { get; set; }

    // Media Controls
    [NotNull]
    public Lockable<int>? forwardSkipTime { get; set; } // = 30;
    [NotNull]
    public Lockable<int>? rewindSkipTime { get; set; } // = 10;
    
    // Audio
    [NotNull]
    public Lockable<bool>? rememberAudioSelections { get; set; } // = true;
    // TODO create type converter for CultureDto
    //  Currently fails since it doesnt have a parameterless constructor
    // public Lockable<CultureDto?>? defaultAudioLanguage { get; set; }
    
    // Subtitles
    // public Lockable<CultureDto?>? defaultSubtitleLanguage { get; set; }
    [NotNull]
    public Lockable<SubtitlePlaybackMode>? subtitleMode { get; set; }
    [NotNull]
    public Lockable<bool>? rememberSubtitleSelections { get; set; } // = true;
    [NotNull]
    public Lockable<int>? subtitleSize { get; set; } // = 80;

    // Other
    [NotNull]
    public Lockable<bool>? autoRotate { get; set; } // true
    [NotNull]
    public Lockable<OrientationLock>? defaultVideoOrientation { get; set; }
    [NotNull]
    public Lockable<bool>? safeAreaInControlsEnabled { get; set; } // = true;
    [NotNull]
    public Lockable<bool>? showCustomMenuLinks { get; set; } // = false;
    [NotNull]
    public Lockable<string[]>? hiddenLibraries { get; set; } // = [];
    [NotNull]
    public Lockable<bool>? disableHapticFeedback { get; set; } // = false;

    // Downloads
    [NotNull]
    public Lockable<DownloadMethod>? downloadMethod { get; set; }
    [NotNull]
    public Lockable<RemuxConcurrentLimit>? remuxConcurrentLimit { get; set; }
    [NotNull]
    public Lockable<bool>? autoDownload { get; set; } // = false;
    [NotNull]
    public Lockable<string>? optimizedVersionsServerUrl { get; set; }
    
    // region Plugins
    // Jellyseerr
    [NotNull]
    public Lockable<string>? jellyseerrServerUrl { get; set; }

    // Marlin Search
    [NotNull]
    public Lockable<SearchEngine>? searchEngine { get; set; } // = SearchEngine.Jellyfin;
    [NotNull]
    public Lockable<string>? marlinServerUrl { get; set; }

    // Popular Lists
    [NotNull]
    public Lockable<bool>? usePopularPlugin { get; set; } // = false;
    [NotNull]
    public Lockable<string[]>? mediaListCollectionIds { get; set; } // = false;
    // endregion Plugins
    
    // Misc.
    [NotNull]
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