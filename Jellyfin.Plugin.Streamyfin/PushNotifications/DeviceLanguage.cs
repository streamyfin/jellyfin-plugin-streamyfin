using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// The language a device says it is in.
/// </summary>
/// <remarks>
/// It arrives from a client, so it is whatever that client sends. A tag that is not a
/// language is taken as none rather than refused: a device that cannot say what language
/// it is in should still receive its notifications, written in the server's.
/// </remarks>
public static class DeviceLanguage
{
    /// <summary>
    /// The longest tag worth keeping. A BCP 47 tag with a script and a region fits well
    /// inside this, and anything longer is a client sending something else.
    /// </summary>
    private const int LongestTag = 35;

    /// <summary>
    /// The shape of a language tag: letters, then subtags of letters or digits. ICU answers
    /// to a good deal more than that, a sentence included, and what it answers with is not
    /// a language.
    /// </summary>
    private static readonly Regex Shaped = new("^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8})*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The tag as it is stored, or <c>null</c> when it is not a language.
    /// </summary>
    /// <param name="tag">What the device sent.</param>
    /// <returns>The tag as .NET names it, or <c>null</c>.</returns>
    /// <remarks>
    /// Stored the way .NET names it, so two devices that spell the same language
    /// differently are one language when the messages are written.
    /// </remarks>
    public static string? Stored(string? tag) => Culture(tag)?.Name;

    /// <summary>
    /// The culture a device's messages are written in.
    /// </summary>
    /// <param name="tag">The stored tag.</param>
    /// <returns>The culture, or <c>null</c> to leave the server's.</returns>
    public static CultureInfo? CultureOf(string? tag) => Culture(tag);

    private static CultureInfo? Culture(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        // Android names a locale fr_FR and JavaScript names it fr-FR. Both are the same
        // device saying the same thing.
        var trimmed = tag.Trim().Replace('_', '-');

        if (trimmed.Length > LongestTag || !Shaped.IsMatch(trimmed))
        {
            return null;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(trimmed);

            // The invariant culture answers to the empty name, and a name that is only
            // separators reaches it as well. It says nothing about a device.
            return string.IsNullOrEmpty(culture.Name) ? null : culture;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
