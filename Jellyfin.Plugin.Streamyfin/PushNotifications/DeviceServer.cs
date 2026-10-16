using System;
using System.Globalization;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// The address a device reaches the server at, and what a notification's poster is
/// fetched from.
/// </summary>
/// <remarks>
/// A server is reached at different addresses by different devices, at home and away, and
/// the server's own idea of its address is often the one nobody outside can use. The
/// address therefore comes from the device, which is the one that just used it.
/// </remarks>
public static class DeviceServer
{
    /// <summary>
    /// How tall a poster is asked for. Android shows the image at the width of the
    /// notification and Jellyfin scales it on the way out, so this is about the bytes a
    /// phone pulls rather than about the size it is shown at.
    /// </summary>
    internal const int PosterHeight = 640;

    /// <summary>
    /// The longest address worth keeping.
    /// </summary>
    private const int LongestAddress = 255;

    /// <summary>
    /// The address as it is stored, or <c>null</c> when it is not one this can fetch from.
    /// </summary>
    /// <param name="address">What the device sent.</param>
    /// <returns>The address without its trailing slash, or <c>null</c>.</returns>
    public static string? Stored(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var trimmed = address.Trim().TrimEnd('/');

        if (trimmed.Length > LongestAddress
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return trimmed;
    }

    /// <summary>
    /// Where a device fetches an item's poster.
    /// </summary>
    /// <param name="serverUrl">The address the device reaches the server at.</param>
    /// <param name="itemId">The item the poster is of.</param>
    /// <returns>The address of the image, or <c>null</c> when there is nowhere to fetch it from.</returns>
    /// <remarks>
    /// Jellyfin serves an item's images without a token, which is what makes this work: the
    /// phone, or Expo on its behalf, fetches the image with no credentials of ours in it.
    /// </remarks>
    public static string? PosterOf(string? serverUrl, Guid itemId)
    {
        var address = Stored(serverUrl);

        if (address is null || itemId.Equals(default))
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{address}/Items/{itemId:N}/Images/Primary?maxHeight={PosterHeight}");
    }
}
