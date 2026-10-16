using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// The text earlier versions of the plugin stored where a value goes (#279).
/// </summary>
/// <remarks>
/// From 0.66 to 0.68.1.0 the default configuration held "Enter jellyseerr server url" for
/// the Seerr address, "Enter marlin server url" or "Enter Marlin server URL" for Marlin's, and
/// "Enter library id(s)" as the one hidden library, and the examples in this repository wrote
/// "Enter Streamystats server URL" too. A server that never set them still has them stored,
/// and 0.70 served them as values: the app tested the Seerr text as an address at every sign
/// in, and the tabs listed the library text as a library. None of them is a value anybody
/// chose, so each reads as absent.
/// </remarks>
internal static class Placeholders
{
    // Compared without case, as 0.66 and 0.67 wrote Marlin's two ways.
    private static readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase)
    {
        "Enter jellyseerr server url",
        "Enter marlin server url",
        "Enter Streamystats server URL",
        "Enter library id(s)"
    };

    /// <summary>
    /// Whether <paramref name="value"/> is one of the placeholders, spaces around it aside.
    /// </summary>
    /// <param name="value">A stored value.</param>
    /// <returns><c>true</c> for a placeholder.</returns>
    internal static bool Has(string? value) => value is not null && _known.Contains(value.Trim());

    /// <summary>
    /// The settings of <paramref name="settings"/> that hold a placeholder, by key, in order.
    /// </summary>
    /// <param name="settings">One level's settings.</param>
    /// <returns>The keys, empty when there is none.</returns>
    internal static IReadOnlyList<string> HeldBy(Settings? settings)
    {
        if (settings is null)
        {
            return [];
        }

        return SettingsSchema.Descriptors
            .Where(descriptor => descriptor.Read(settings) switch
            {
                string text => Has(text),
                string[] values => values.Any(Has),
                _ => false
            })
            .Select(descriptor => descriptor.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
