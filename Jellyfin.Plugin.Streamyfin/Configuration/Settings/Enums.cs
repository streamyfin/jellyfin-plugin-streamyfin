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
    Jellyfin
};

[JsonConverter(typeof(StringEnumConverter))]
public enum DownloadMethod
{
    optimized,
    remux
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

[JsonConverter(typeof(StringEnumConverter))]
public enum DownloadQuality
{
    Original,
    Low,
    High
}

[JsonConverter(typeof(StringEnumConverter))]
public enum SidebarLinkType
{
    library,
    collection
}

// Limit Int range. Don't use Converter for this since we want them to enter int value
public enum RemuxConcurrentLimit
{
    One = 1,
    Two = 2,
    Three = 3,
    Four = 4,
}

public enum Bitrate
{
    _250KB = 250000,
    _500KB = 500000,
    _1MB = 1000000,
    _2MB = 2000000,
    _4MB = 4000000,
    _8MB = 8000000,
};
