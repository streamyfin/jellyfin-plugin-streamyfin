using System;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Whether something an administrator typed is an address at all.
/// </summary>
/// <remarks>
/// The shape only. Whether anything answers there is what a probe asks.
/// </remarks>
public static class WebAddress
{
    /// <summary>
    /// Whether this is a whole http or https address.
    /// </summary>
    /// <param name="typed">The address, as it was typed.</param>
    /// <param name="address">The address, when it is one.</param>
    /// <returns>Whether it is.</returns>
    /// <remarks>
    /// Absolute, and http or https. <c>file:</c> would have the server read its own
    /// disk, and <c>192.168.1.5:3000</c> is not something the app can request.
    /// </remarks>
    public static bool Parses(string? typed, out Uri? address)
    {
        address = null;

        if (string.IsNullOrWhiteSpace(typed) || !Uri.TryCreate(typed.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        address = parsed;
        return true;
    }
}
