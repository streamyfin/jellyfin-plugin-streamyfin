using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The plugin declares what the app reads, and nothing else.
///
/// A key the plugin does not declare resolves <c>locked</c> to <c>undefined</c> in the
/// app, so the lock never fires and no value is ever pushed. A key the plugin declares
/// under a name the app does not read is worse, because it looks like it works. Both
/// have shipped, which is what <c>docs/rewrite/settings-parity.md</c> records.
/// </summary>
public class SettingsParityTests
{
    /// <summary>
    /// Keys the app reads that the plugin deliberately does not declare, and why.
    /// </summary>
    /// <remarks>
    /// Deleting an entry from here is how a setting comes under administrator control.
    /// A key in neither this list nor <see cref="Settings"/> fails the first test, so
    /// no key can arrive without someone deciding about it.
    /// </remarks>
    private static readonly Dictionary<string, string> NotDeclared = new(StringComparer.Ordinal)
    {
        ["defaultAudioLanguage"] = "not yet declared",
        ["defaultSubtitleLanguage"] = "not yet declared",
        ["downloadQuality"] =
            "Declared once the app can read it back. The app types it as DownloadOption, "
            + "which is { label, value }, and the generic fallback in normalizePluginValue "
            + "only rebuilds { key, value }, so a value declared today would arrive in a "
            + "shape the app cannot use. Either the plugin sends the scalar and the app "
            + "gains a normalizer case, or DownloadOption gains a key.",
        ["playbackSpeedPerMedia"] =
            "Not a setting. A map the player writes by itself, keyed by item id, so "
            + "there is nothing an administrator could put in it.",
        ["playbackSpeedPerShow"] =
            "Not a setting. A map the player writes by itself, keyed by series id.",
    };

    /// <summary>
    /// Declared defaults that knowingly differ from the app's, and why.
    /// </summary>
    /// <remarks>
    /// This list should stay empty or nearly so. An entry is a promise that someone
    /// weighed the difference, not a place to put a default that turned out to be
    /// inconvenient to fix.
    /// </remarks>
    private static readonly Dictionary<string, string> KnownDisagreements = new(StringComparer.Ordinal)
    {
        ["subtitlesOnMute"] =
            "The app's published develop still defaults this to false. The true here "
            + "matches the branch of streamyfin/streamyfin#1900, which pull request #109 "
            + "was deliberately aligned with. Delete this entry the day that branch merges."
    };

    /// <summary>
    /// Keys the plugin declares that the app's published branch does not read yet, and
    /// why that is deliberate rather than the mistake of pull request #109.
    /// </summary>
    /// <remarks>
    /// An entry here is a bet that the app change lands. It is safe only while the
    /// plugin ships no default for the key, or ships one the app agrees with once it
    /// catches up, since an unlocked default is applied whether the app understands the
    /// key or not.
    /// </remarks>
    private static readonly Dictionary<string, string> DeclaredAheadOfTheApp = new(StringComparer.Ordinal)
    {
        ["subtitlesOnMuteAllowRestart"] =
            "Lives on the branch of streamyfin/streamyfin#1900, which is open and "
            + "mergeable. Declared by pull request #109 on purpose so the two halves "
            + "could land in either order. Delete this entry once that branch merges "
            + "and the manifest is regenerated."
    };

    private sealed record ManifestEntry(
        string Key,
        string Type,
        JsonElement Default,
        bool HasDefault,
        string? NoDefaultReason,
        JsonElement WireDefault,
        string? WireNote);

    private static IReadOnlyList<ManifestEntry> Manifest()
    {
        var assembly = typeof(SettingsParityTests).Assembly;
        var resource = assembly
            .GetManifestResourceNames()
            .Single(name => name.EndsWith("AppSettingsManifest.json", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);

        return JsonSerializer.Deserialize<List<ManifestEntry>>(
            reader.ReadToEnd(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static HashSet<string> DeclaredKeys() =>
        typeof(Settings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Every setting the app reads has been decided about: declared, or listed as
    /// deliberately not declared with the reason written down.
    /// </summary>
    [Fact]
    public void EverySettingTheAppReadsHasBeenDecidedAbout()
    {
        var declared = DeclaredKeys();

        var undecided = Manifest()
            .Select(entry => entry.Key)
            .Where(key => !declared.Contains(key) && !NotDeclared.ContainsKey(key))
            .ToArray();

        Assert.True(
            undecided.Length == 0,
            "Neither declared nor excused:\n  " + string.Join("\n  ", undecided));
    }

    /// <summary>
    /// Every key the plugin declares is one the app actually reads.
    /// </summary>
    /// <remarks>
    /// This is the one that catches the mistake in pull request #109, where the plugin
    /// declared <c>autoSubtitlesOnMute</c> while the app read <c>subtitlesOnMute</c>. It
    /// shipped two properties nothing reads, and the lock they existed to enable still
    /// did nothing.
    /// </remarks>
    [Fact]
    public void EveryKeyThePluginDeclaresIsOneTheAppReads()
    {
        var known = Manifest().Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        var unknown = DeclaredKeys()
            .Where(key => !known.Contains(key) && !DeclaredAheadOfTheApp.ContainsKey(key))
            .ToArray();

        Assert.True(
            unknown.Length == 0,
            "Declared, but the app reads no such key:\n  " + string.Join("\n  ", unknown));
    }

    /// <summary>
    /// A declared default equals the app's own.
    /// </summary>
    /// <remarks>
    /// An unlocked plugin value is applied exactly once as a default, so a disagreement
    /// here does not sit there harmlessly: it silently flips the setting for every user
    /// who has not already chosen one.
    /// </remarks>
    [Fact]
    public void ADeclaredDefaultEqualsTheAppsOwn()
    {
        var settings = PluginConfiguration.DefaultSettings();
        var options = new SerializationHelper().GetJsonSerializerOptions();
        var properties = typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var disagreements = new List<string>();

        foreach (var entry in Manifest())
        {
            if (KnownDisagreements.ContainsKey(entry.Key))
            {
                continue;
            }

            var property = Array.Find(properties, candidate => candidate.Name == entry.Key);
            if (property is null)
            {
                continue;
            }

            var declared = property.GetValue(settings);
            if (declared is null)
            {
                // Declaring no default is always allowed. It means the plugin proposes
                // nothing and the app keeps its own, which is the quiet option.
                continue;
            }

            if (entry.NoDefaultReason == "platform")
            {
                // The app has more than one default for this, chosen by platform. One
                // number here would be pushed to every device and flatten a difference
                // the app makes deliberately.
                disagreements.Add(
                    $"{entry.Key}: the app's default varies by platform, so the plugin must declare none");
                continue;
            }

            if (!entry.HasDefault)
            {
                // The app ships nothing, so there is nothing to disagree with and an
                // administrator's starting value is welcome.
                continue;
            }

            var value = declared.GetType().GetProperty("value")!.GetValue(declared);
            var written = JsonSerializer.Serialize(value, options);

            // normalizePluginValue reshapes a few keys on the way into the app, so for
            // those the plugin has to send the wire form rather than the stored one.
            var expected = entry.WireNote is null ? entry.Default : entry.WireDefault;

            if (!Equivalent(written, expected))
            {
                var because = entry.WireNote is null ? string.Empty : $" ({entry.WireNote})";
                disagreements.Add($"{entry.Key}: app {expected}, plugin {written}{because}");
            }
        }

        Assert.True(
            disagreements.Count == 0,
            "Defaults that disagree with the app:\n  " + string.Join("\n  ", disagreements));
    }

    /// <summary>
    /// A disagreement is only excused while the key it names still exists.
    /// </summary>
    /// <remarks>
    /// Without this, an excuse outlives the property it was written for and the next
    /// person reads a reason for something that is no longer there.
    /// </remarks>
    [Fact]
    public void EveryExcusedDisagreementNamesASettingThatExists()
    {
        var known = Manifest().Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        var declared = DeclaredKeys();

        var stale = NotDeclared.Keys
            .Concat(KnownDisagreements.Keys)
            .Where(key => !known.Contains(key))
            .Concat(DeclaredAheadOfTheApp.Keys.Where(key => !declared.Contains(key)))
            .ToArray();

        Assert.True(
            stale.Length == 0,
            "Excused, but no such setting exists:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// An enum reaches the app under the string the app compares against.
    /// </summary>
    /// <remarks>
    /// Three of these cannot be spelled as a C# member name: <c>5.1</c> and
    /// <c>gpu-next</c> are not identifiers, and <c>default</c> is a keyword. Renaming a
    /// member to something legal without saying so on the wire is silent: the build
    /// passes, the app receives a string it has no case for, and the setting does
    /// nothing.
    /// </remarks>
    [Theory]
    [InlineData(AudioTranscodeMode.Auto, "\"auto\"")]
    [InlineData(AudioTranscodeMode.ForceStereo, "\"stereo\"")]
    [InlineData(AudioTranscodeMode.Allow51, "\"5.1\"")]
    [InlineData(AudioTranscodeMode.AllowAll, "\"passthrough\"")]
    [InlineData(MpvVoDriver.GpuNext, "\"gpu-next\"")]
    [InlineData(MpvVoDriver.Gpu, "\"gpu\"")]
    [InlineData(MpvCacheMode.Auto, "\"auto\"")]
    [InlineData(MpvCacheMode.Yes, "\"yes\"")]
    [InlineData(MpvCacheMode.No, "\"no\"")]
    [InlineData(TVTypographyScale.Small, "\"small\"")]
    [InlineData(TVTypographyScale.Default, "\"default\"")]
    [InlineData(TVTypographyScale.Large, "\"large\"")]
    [InlineData(TVTypographyScale.ExtraLarge, "\"extraLarge\"")]
    [InlineData(DownloadQuality.Original, "\"original\"")]
    [InlineData(DownloadQuality.High, "\"high\"")]
    [InlineData(DownloadQuality.Low, "\"low\"")]
    [InlineData(SubtitleAlignX.Left, "\"left\"")]
    [InlineData(SubtitleAlignX.Center, "\"center\"")]
    [InlineData(SubtitleAlignY.Bottom, "\"bottom\"")]
    [InlineData(SubtitleAlignY.Top, "\"top\"")]
    [InlineData(DeviceProfile.Expo, "\"Expo\"")]
    public void AnEnumReachesTheAppUnderTheStringTheAppCompares(object member, string expected)
    {
        var written = JsonSerializer.Serialize(
            member,
            member.GetType(),
            new SerializationHelper().GetJsonSerializerOptions());

        Assert.Equal(expected, written);
    }

    /// <summary>
    /// The two enums the app compares as numbers are written as numbers.
    /// </summary>
    /// <remarks>
    /// Same reason <c>OrientationLock</c>, <c>Bitrate</c> and <c>SubtitlePlaybackMode</c>
    /// already have a number converter registered. The default is the member name, and a
    /// name where the app switches on a number matches nothing.
    /// </remarks>
    [Theory]
    [InlineData(VideoPlayer.MPV, "0")]
    [InlineData(VideoPlayer.ExoPlayer, "1")]
    [InlineData(VideoPlayer.Native, "2")]
    [InlineData(InactivityTimeout.Disabled, "0")]
    [InlineData(InactivityTimeout.OneMinute, "60000")]
    [InlineData(InactivityTimeout.FiveMinutes, "300000")]
    [InlineData(InactivityTimeout.TwentyFourHours, "86400000")]
    public void AnEnumTheAppComparesAsANumberIsWrittenAsANumber(object member, string expected)
    {
        var written = JsonSerializer.Serialize(
            member,
            member.GetType(),
            new SerializationHelper().GetJsonSerializerOptions());

        Assert.Equal(expected, written);
    }

    // Through the serializer both sides use. Comparing CLR values would pass for an
    // enum written as a number where the app expects its name.
    private static bool Equivalent(string written, JsonElement expected)
    {
        using var document = JsonDocument.Parse(written);
        var actual = document.RootElement;

        // 2 and 2.0 are the same JSON number and the app parses JSON, so comparing the
        // text would fail on a C# double that happens to serialize without a fraction.
        if (actual.ValueKind == JsonValueKind.Number && expected.ValueKind == JsonValueKind.Number)
        {
            return actual.GetDouble() == expected.GetDouble();
        }

        return JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(expected);
    }
}
