#pragma warning disable CA1008

using System.Runtime.Serialization;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;

// Aliased rather than imported whole: System.Text.Json.Serialization also declares a
// JsonConverter attribute, and every enum below already carries Newtonsoft's.
using JsonStringEnumMemberName = System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute;

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
     * Both landscape directions, letting the device rotate between them.
     */
    Landscape = 5,
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

// Two attributes per member and not one. EnumMember is what Newtonsoft's
// StringEnumConverter reads, for the YAML and the generated JSON schema.
// JsonStringEnumMemberName is what System.Text.Json reads, for what the app
// receives. A member carrying only one of the two is written differently by the
// two paths, and the difference is invisible until a device gets the wrong string.

[JsonConverter(typeof(StringEnumConverter))]
public enum AudioTranscodeMode
{
    [EnumMember(Value = "auto")]
    [JsonStringEnumMemberName("auto")]
    Auto,

    [EnumMember(Value = "stereo")]
    [JsonStringEnumMemberName("stereo")]
    ForceStereo,

    // "5.1" is not a C# identifier, so the member name and the wire value differ.
    [EnumMember(Value = "5.1")]
    [JsonStringEnumMemberName("5.1")]
    Allow51,

    [EnumMember(Value = "passthrough")]
    [JsonStringEnumMemberName("passthrough")]
    AllowAll
};

[JsonConverter(typeof(StringEnumConverter))]
public enum MpvCacheMode
{
    [EnumMember(Value = "auto")]
    [JsonStringEnumMemberName("auto")]
    Auto,

    [EnumMember(Value = "yes")]
    [JsonStringEnumMemberName("yes")]
    Yes,

    [EnumMember(Value = "no")]
    [JsonStringEnumMemberName("no")]
    No
};

[JsonConverter(typeof(StringEnumConverter))]
public enum MpvVoDriver
{
    // "gpu-next" is not a C# identifier.
    [EnumMember(Value = "gpu-next")]
    [JsonStringEnumMemberName("gpu-next")]
    GpuNext,

    [EnumMember(Value = "gpu")]
    [JsonStringEnumMemberName("gpu")]
    Gpu
};

[JsonConverter(typeof(StringEnumConverter))]
public enum TVTypographyScale
{
    [EnumMember(Value = "small")]
    [JsonStringEnumMemberName("small")]
    Small,

    // "default" is a C# keyword, so the member is Default and the wire value is not.
    [EnumMember(Value = "default")]
    [JsonStringEnumMemberName("default")]
    Default,

    [EnumMember(Value = "large")]
    [JsonStringEnumMemberName("large")]
    Large,

    [EnumMember(Value = "extraLarge")]
    [JsonStringEnumMemberName("extraLarge")]
    ExtraLarge
};

[JsonConverter(typeof(StringEnumConverter))]
public enum DownloadQuality
{
    [EnumMember(Value = "original")]
    [JsonStringEnumMemberName("original")]
    Original,

    [EnumMember(Value = "high")]
    [JsonStringEnumMemberName("high")]
    High,

    [EnumMember(Value = "low")]
    [JsonStringEnumMemberName("low")]
    Low
};

[JsonConverter(typeof(StringEnumConverter))]
public enum SubtitleAlignX
{
    [EnumMember(Value = "left")]
    [JsonStringEnumMemberName("left")]
    Left,

    [EnumMember(Value = "center")]
    [JsonStringEnumMemberName("center")]
    Center,

    [EnumMember(Value = "right")]
    [JsonStringEnumMemberName("right")]
    Right
};

[JsonConverter(typeof(StringEnumConverter))]
public enum SubtitleAlignY
{
    [EnumMember(Value = "top")]
    [JsonStringEnumMemberName("top")]
    Top,

    [EnumMember(Value = "center")]
    [JsonStringEnumMemberName("center")]
    Center,

    [EnumMember(Value = "bottom")]
    [JsonStringEnumMemberName("bottom")]
    Bottom
};

/// <summary>
/// Which video player the app uses. Compared as a number by the app.
/// </summary>
public enum VideoPlayer
{
    MPV = 0,
    ExoPlayer = 1,
    Native = 2
};

/// <summary>
/// How long the TV app waits before signing out, in milliseconds.
/// </summary>
public enum InactivityTimeout
{
    Disabled = 0,
    OneMinute = 60000,
    FiveMinutes = 300000,
    FifteenMinutes = 900000,
    ThirtyMinutes = 1800000,
    OneHour = 3600000,
    FourHours = 14400000,
    TwentyFourHours = 86400000
};
