using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// The app's own default for each setting, as the plugin writes the value.
/// </summary>
/// <remarks>
/// Read from <c>AppSettingsManifest.json</c>, which <c>scripts/app-settings-manifest.js</c>
/// generates from the app's source and a weekly workflow keeps current. The plugin
/// declares a default for some settings only, because a declared default is pushed once to
/// every user; this is what the app does when nothing is pushed, for all of them, so the
/// form can put a setting back to it. A default the app picks per platform is not one, and
/// a key is read under every name the app reads the setting under.
/// </remarks>
public static class AppDefaults
{
    private static readonly Lazy<Dictionary<string, JsonElement>> _byKey = new(Read);

    // The default of the apps in the field for a key the app's develop no longer declares and
    // the plugin still serves them. The manifest, written from develop, has no line for it any
    // more, and Reset would leave a setting those apps still read.
    private static readonly Dictionary<string, JsonElement> _keptForTheField = new(StringComparer.Ordinal)
    {
        // The app 0.55 caps auto play at 3 episodes in a row.
        ["maxAutoPlayEpisodeCount"] = JsonSerializer.SerializeToElement(3),

        // The app 0.55 offers HEVC to no Chromecast unless asked.
        ["enableH265ForChromecast"] = JsonSerializer.SerializeToElement(false),
    };

    /// <summary>
    /// The app's default for a setting, or <c>null</c> when the app has none to give.
    /// </summary>
    /// <param name="key">The setting's key.</param>
    /// <returns>The value, written the way the plugin stores it.</returns>
    public static JsonElement? For(string key) =>
        _byKey.Value.TryGetValue(key, out var value) ? value
        : _keptForTheField.TryGetValue(key, out var kept) ? kept
        : null;

    private static Dictionary<string, JsonElement> Read()
    {
        var byKey = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        using var stream = typeof(AppDefaults).Assembly.GetManifestResourceStream("Jellyfin.Plugin.Streamyfin.AppSettingsManifest.json");
        if (stream is null)
        {
            return byKey;
        }

        using var manifest = JsonDocument.Parse(stream);
        foreach (var entry in manifest.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("hasDefault", out var has) || !has.GetBoolean())
            {
                continue;
            }

            // normalizePluginValue reshapes a few keys on the way into the app, so for those
            // the plugin's value is the wire form rather than the app's own.
            var reshaped = entry.TryGetProperty("wireNote", out var note) && note.ValueKind != JsonValueKind.Null;
            var value = entry.GetProperty(reshaped ? "wireDefault" : "default").Clone();

            foreach (var name in NamesOf(entry).Where(name => !name.Contains('.', StringComparison.Ordinal)))
            {
                byKey.TryAdd(name, value);
            }
        }

        return byKey;
    }

    private static IEnumerable<string> NamesOf(JsonElement entry)
    {
        yield return entry.GetProperty("key").GetString()!;

        if (entry.TryGetProperty("wireNames", out var names) && names.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in names.EnumerateArray())
            {
                yield return name.GetString()!;
            }
        }
    }
}
