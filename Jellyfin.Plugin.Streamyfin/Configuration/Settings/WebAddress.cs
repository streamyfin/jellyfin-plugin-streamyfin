using System;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Whether something an administrator typed is an address at all.
/// </summary>
/// <remarks>
/// The shape only. Whether anything answers there is a different question, and one
/// only a probe can ask.
///
/// <para>
/// Here rather than beside the probe, so the validation that refuses a bad address and
/// the probe that would open it agree without the settings folder and the integrations
/// folder pointing at each other.
/// </para>
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
    /// Absolute, and http or https. A <c>file:</c> address would have the server read
    /// its own disk and report whether it succeeded, which is a probe answering a
    /// question nobody asked, and <c>192.168.1.5:3000</c> is a host and a port that the
    /// app cannot turn into a request.
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
