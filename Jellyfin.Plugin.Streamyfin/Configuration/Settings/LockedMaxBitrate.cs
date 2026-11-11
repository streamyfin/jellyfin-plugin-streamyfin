namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// The playback quality an earlier version locked empty, which it labelled Max.
/// </summary>
/// <remarks>
/// This version names Max and leaves the empty value to the app's own default, Auto, so
/// what an earlier version locked empty is locked at Max: the empty value could only mean
/// Max when it was written.
/// </remarks>
public static class LockedMaxBitrate
{
    /// <summary>
    /// Locks the quality at Max where one level's settings lock it empty.
    /// </summary>
    /// <param name="settings">The server's, a group's or a person's settings.</param>
    /// <returns>Whether they changed.</returns>
    public static bool CarryOver(Settings? settings)
    {
        if (settings?.defaultBitrate is not { locked: true, value: null } quality)
        {
            return false;
        }

        quality.value = Bitrate.Max;
        return true;
    }
}
