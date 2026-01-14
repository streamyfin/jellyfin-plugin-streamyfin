#pragma warning disable CA1008

using Newtonsoft.Json.Converters;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.Streamyfin.Configuration;


[JsonConverter(typeof(StringEnumConverter))]
public enum DeviceProfile
{
    Expo,
    Native,
    Old
};

[JsonConverter(typeof(StringEnumConverter))]
public enum SearchEngine
{
    Marlin,
    Jellyfin,
    Streamystats
};

[JsonConverter(typeof(StringEnumConverter))]
public enum OrientationLock {
    /**
     * The default orientation. On iOS, this will allow all orientations except `Orientation.PORTRAIT_DOWN`.
     * On Android, this lets the system decide the best orientation.
     */
    Default = 0,
    /**
     * Right-side up portrait only.
     */
    PortraitUp = 3,
    /**
     * Left landscape only.
     */
    LandscapeLeft = 6,
    /**
     * Right landscape only.
     */
    LandscapeRight = 7,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum DisplayType
{
    row,
    list
};

[JsonConverter(typeof(StringEnumConverter))]
public enum CardStyle
{
    compact,
    detailed
};

[JsonConverter(typeof(StringEnumConverter))]
public enum ImageStyle
{
    poster,
    cover
};

public enum Bitrate
{
    _250KB = 250000,
    _500KB = 500000,
    _1MB = 1000000,
    _2MB = 2000000,
    _4MB = 4000000,
    _8MB = 8000000,
};

// These enums were removed from Jellyfin.Data.Enums in Jellyfin 10.11
// Kept here for backward compatibility
[JsonConverter(typeof(StringEnumConverter))]
public enum SubtitlePlaybackMode
{
    Default = 0,
    Always = 1,
    OnlyForced = 2,
    None = 3,
    Smart = 4
}

[JsonConverter(typeof(StringEnumConverter))]
public enum SortOrder
{
    Ascending = 0,
    Descending = 1
}

[JsonConverter(typeof(StringEnumConverter))]
public enum SegmentSkipMode
{
    none = 0,
    ask = 1,
    auto = 2
}
