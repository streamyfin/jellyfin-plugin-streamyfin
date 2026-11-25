namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// The playback quality an earlier version stored empty, which it labelled Max.
/// </summary>
/// <remarks>
/// This version names Max and leaves the empty value to the app's own default, Auto, so
/// what an earlier version stored empty where an administrator chose it is set at Max: the
/// empty value could only mean Max when it was written.
/// </remarks>
public static class LockedMaxBitrate
{
    /// <summary>
    /// Sets the quality at Max where one level's settings hold it empty by an administrator's choice.
    /// </summary>
    /// <param name="settings">The server's, a group's or a person's settings.</param>
    /// <param name="aboveServer">
    /// Whether they are a group's or a person's. Those hold only what an administrator picked there,
    /// so an empty value is Max there, locked or not. The server's settings ship an unlocked empty
    /// quality, the app's default then too, so only a locked one is carried there.
    /// </param>
    /// <returns>Whether they changed.</returns>
    public static bool CarryOver(Settings? settings, bool aboveServer = false)
    {
        if (settings?.defaultBitrate is not { value: null } quality || !(quality.locked || aboveServer))
        {
            return false;
        }

        quality.value = Bitrate.Max;
        return true;
    }
}
