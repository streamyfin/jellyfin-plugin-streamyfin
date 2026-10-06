#pragma warning disable CA1008

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;

// Aliased rather than imported whole: System.Text.Json.Serialization also declares a
// JsonConverter attribute, and every enum below already carries Newtonsoft's.
using JsonStringEnumMemberName = System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute;

namespace Jellyfin.Plugin.Streamyfin.Configuration;


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
    [Display(Name = "Follow device orientation")]
    Default = 0,
    /**
     * Right-side up portrait only.
     */
    PortraitUp = 3,
    /**
     * Both landscape directions, letting the device rotate between them.
     */
    // The app's own orientation picker calls this "Landscape auto", and Default "Follow
    // device orientation". Derived from the names, the two screens would name the same
    // choices differently; the other members derive as the app labels them.
    [Display(Name = "Landscape auto")]
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

// Labelled and ordered as the app's quality picker, BITRATES in BitrateSelector.tsx:
// Max, which is null here, then the fastest first. The values are what travels.
public enum Bitrate
{
    [Display(Name = "8 Mb/s")]
    _8MB = 8000000,
    [Display(Name = "4 Mb/s")]
    _4MB = 4000000,
    [Display(Name = "2 Mb/s")]
    _2MB = 2000000,
    [Display(Name = "1 Mb/s")]
    _1MB = 1000000,
    [Display(Name = "500 Kb/s")]
    _500KB = 500000,
    [Display(Name = "250 Kb/s")]
    _250KB = 250000,
};

// These enums were removed from Jellyfin.Data.Enums in Jellyfin 10.11
// Kept here for backward compatibility
[JsonConverter(typeof(StringEnumConverter))]
// Declared in the order the app's subtitle mode picker lists them, SubtitleToggles.tsx,
// which is the dropdown's order. The numbers are what storage keeps.
public enum SubtitlePlaybackMode
{
    Default = 0,
    Smart = 4,
    OnlyForced = 2,
    Always = 1,
    None = 3
}

[JsonConverter(typeof(StringEnumConverter))]
public enum SortOrder
{
    Ascending = 0,
    Descending = 1
}

[JsonConverter(typeof(StringEnumConverter))]
// Labelled as the app's segment skip page labels them.
public enum SegmentSkipMode
{
    [Display(Name = "None")]
    none = 0,
    [Display(Name = "Ask to skip")]
    ask = 1,
    [Display(Name = "Skip")]
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
    [Display(Name = "Allow 5.1")]
    Allow51,

    [EnumMember(Value = "passthrough")]
    [JsonStringEnumMemberName("passthrough")]
    [Display(Name = "Passthrough")]
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
    [Display(Name = "Enabled")]
    Yes,

    [EnumMember(Value = "no")]
    [JsonStringEnumMemberName("no")]
    [Display(Name = "Disabled")]
    No
};

[JsonConverter(typeof(StringEnumConverter))]
public enum MpvVoDriver
{
    // "gpu-next" is not a C# identifier.
    [EnumMember(Value = "gpu-next")]
    [JsonStringEnumMemberName("gpu-next")]
    [Display(Name = "gpu-next (Recommended)")]
    GpuNext,

    [EnumMember(Value = "gpu")]
    [JsonStringEnumMemberName("gpu")]
    [Display(Name = "gpu")]
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
/// How long the TV app waits before signing out, in milliseconds.
/// </summary>
public enum InactivityTimeout
{
    Disabled = 0,
    [Display(Name = "1 minute")]
    OneMinute = 60000,
    [Display(Name = "5 minutes")]
    FiveMinutes = 300000,
    [Display(Name = "15 minutes")]
    FifteenMinutes = 900000,
    [Display(Name = "30 minutes")]
    ThirtyMinutes = 1800000,
    [Display(Name = "1 hour")]
    OneHour = 3600000,
    [Display(Name = "4 hours")]
    FourHours = 14400000,
    [Display(Name = "24 hours")]
    TwentyFourHours = 86400000
};
