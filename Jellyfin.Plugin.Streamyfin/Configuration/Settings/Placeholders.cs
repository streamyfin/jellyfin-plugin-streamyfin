using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// The example text earlier versions of the plugin stored where a value goes (#279).
/// </summary>
/// <remarks>
/// Up to 0.68.1.0, since 0.38.0.0, the default configuration held "Enter jellyseerr server url"
/// for the Seerr address, "Enter marlin server url" for Marlin's ("Enter Marlin server URL" from
/// 0.67), and "Enter library id(s)" as the one hidden library; examples this repository shipped
/// also wrote "Enter Streamystats server URL". A server that never set them still has them
/// stored, and 0.70 served them as values: the app tested the Seerr text as an address at every
/// sign in, and the tabs listed the library text as a library. Stored, each is taken out; sent in
/// a document, it is refused by name, since the document replaces the whole configuration and
/// taking the text out would take the server's real value away without a word.
/// </remarks>
internal static class Placeholders
{
    // Compared without case, as Marlin's was written two ways.
    private static readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase)
    {
        "Enter jellyseerr server url",
        "Enter marlin server url",
        "Enter Streamystats server URL",
        "Enter library id(s)"
    };

    /// <summary>
    /// Whether <paramref name="value"/> is the example text, spaces around it aside.
    /// </summary>
    /// <param name="value">A stored value.</param>
    /// <returns><c>true</c> for the example text.</returns>
    internal static bool Has(string? value) => value is not null && _known.Contains(value.Trim());

    // The settings the example text was stored in: the addresses, and the hidden libraries.
    private static IEnumerable<SettingDescriptor> StoredIn => SettingsSchema.Descriptors
        .Where(descriptor => descriptor.IsWebAddress || descriptor.Key == "hiddenLibraries");

    /// <summary>
    /// Takes the example text out of <paramref name="settings"/>, in place: an address holding it
    /// goes the way an empty one does, and the hidden libraries lose it, keeping a lock an
    /// administrator put on them on an empty list, since that lock kept everyone from hiding any.
    /// </summary>
    /// <param name="settings">One level's settings, which may be null.</param>
    /// <returns>The keys of the settings changed, in order.</returns>
    internal static IReadOnlyList<string> TakeOut(Settings? settings)
    {
        if (settings is null)
        {
            return [];
        }

        var taken = new List<string>();

        foreach (var descriptor in StoredIn.Where(descriptor => descriptor.IsWebAddress))
        {
            if (descriptor.Read(settings) is string address && Has(address))
            {
                descriptor.Property.SetValue(settings, null);
                taken.Add(descriptor.Key);
            }
        }

        if (settings.hiddenLibraries?.value is { } hidden && hidden.Any(Has))
        {
            var kept = hidden.Where(id => !Has(id)).ToArray();

            if (kept.Length == 0 && !settings.hiddenLibraries.locked)
            {
                settings.hiddenLibraries = null;
            }
            else
            {
                settings.hiddenLibraries.value = kept;
            }

            taken.Add("hiddenLibraries");
        }

        if (settings.seerr?.serverUrl?.value is { } block && Has(block))
        {
            settings.seerr.serverUrl = null;
            taken.Add("seerr.serverUrl");
        }

        return [.. taken.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Why a document cannot be stored while it still holds the example text: one sentence per
    /// setting holding it, in declaration order.
    /// </summary>
    /// <param name="settings">The settings as they were written.</param>
    /// <returns>The message, or <c>null</c> when none holds it.</returns>
    internal static string? Problem(Settings? settings)
    {
        if (settings is null)
        {
            return null;
        }

        var problems = new List<string>();

        foreach (var descriptor in StoredIn)
        {
            var example = descriptor.Read(settings) switch
            {
                string text when Has(text) => text.Trim(),
                string[] values => values.FirstOrDefault(Has)?.Trim(),
                _ => null
            };

            if (example is not null)
            {
                problems.Add(Sentence(descriptor.DisplayName ?? descriptor.Key, example));
            }
        }

        if (settings.seerr?.serverUrl?.value is { } block && Has(block))
        {
            problems.Add(Sentence("seerr.serverUrl", block.Trim()));
        }

        return problems.Count == 0 ? null : string.Join(" ", problems);
    }

    private static string Sentence(string setting, string example) =>
        $"{setting} still holds the example text \"{example}\": write a value, or leave the setting out.";
}
