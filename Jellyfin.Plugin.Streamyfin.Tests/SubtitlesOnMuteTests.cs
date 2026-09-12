using System.Text.Json;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The two mute subtitle settings are a contract with the app, the same way the orientation
/// values are a contract with Expo. The app reads them by name off the raw configuration:
/// <c>settings.subtitlesOnMute</c> and <c>settings.subtitlesOnMuteAllowRestart</c> in
/// <c>utils/atoms/settings.ts</c>, and their <c>locked</c> flags in
/// <c>components/settings/SubtitleToggles.tsx</c>.
///
/// A key declared under any other name is not a broken feature that shows an error. It is a
/// setting an admin can fill in, lock, and watch do nothing, which is how this pull request
/// spent its first three weeks.
/// </summary>
public class SubtitlesOnMuteTests
{
    /// <summary>
    /// The keys reach the app under exactly the names it reads.
    /// </summary>
    [Fact]
    public void TheKeysAreServedUnderTheNamesTheAppReads()
    {
        var helper = new SerializationHelper();

        var json = helper.SerializeToJson(new Settings
        {
            subtitlesOnMute = new Lockable<bool> { locked = true, value = true },
            subtitlesOnMuteAllowRestart = new Lockable<bool> { locked = true, value = true }
        });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("subtitlesOnMute", out var onMute));
        Assert.True(onMute.GetProperty("locked").GetBoolean());
        Assert.True(onMute.GetProperty("value").GetBoolean());

        Assert.True(root.TryGetProperty("subtitlesOnMuteAllowRestart", out var allowRestart));
        Assert.True(allowRestart.GetProperty("locked").GetBoolean());
        Assert.True(allowRestart.GetProperty("value").GetBoolean());
    }

    /// <summary>
    /// The defaults match the app's own, <c>true</c> and <c>false</c> at
    /// <c>utils/atoms/settings.ts</c>. This matters more than it looks: an unlocked plugin
    /// value is applied once as a default, so a disagreement here silently flips the setting
    /// for every user who has not already chosen one.
    /// </summary>
    [Fact]
    public void TheDefaultsAgreeWithTheApp()
    {
        var settings = PluginConfiguration.DefaultConfig().settings;

        Assert.NotNull(settings);
        Assert.True(settings!.subtitlesOnMute?.value);
        Assert.False(settings.subtitlesOnMuteAllowRestart?.value);
    }

    /// <summary>
    /// Neither is locked by default. A plugin that ships a lock takes the choice away from
    /// every user on every server that installs it without configuring anything.
    /// </summary>
    [Fact]
    public void NeitherIsLockedByDefault()
    {
        var settings = PluginConfiguration.DefaultConfig().settings;

        Assert.NotNull(settings);
        Assert.False(settings!.subtitlesOnMute?.locked);
        Assert.False(settings.subtitlesOnMuteAllowRestart?.locked);
    }
}
