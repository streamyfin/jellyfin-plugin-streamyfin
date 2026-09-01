using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Db;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Turns what is stored into the settings one caller actually receives.
/// </summary>
/// <remarks>
/// <see cref="SettingsResolver"/> is the rule and knows nothing about storage. This
/// reads the stored levels, which are JSON, and hands them to it in order.
///
/// <para>
/// A level that cannot be read is skipped with a warning rather than thrown. A group
/// whose JSON someone corrupted should cost that group's overrides, not every
/// setting for every user in it.
/// </para>
/// </remarks>
/// <param name="serialization">The plugin's serializer.</param>
/// <param name="logger">Optional logger.</param>
public sealed class SettingsResolutionService(
    SerializationHelper serialization,
    ILogger<SettingsResolutionService>? logger = null)
{
    private readonly SerializationHelper _serialization = serialization;
    private readonly ILogger<SettingsResolutionService>? _logger = logger;

    /// <summary>
    /// Resolves the three levels for one caller.
    /// </summary>
    /// <param name="global">What the server declares for everyone.</param>
    /// <param name="groups">The caller's groups, least specific first.</param>
    /// <param name="userOverride">Anything targeted at the caller alone.</param>
    /// <returns>The settings the caller receives.</returns>
    public Settings Resolve(
        Settings? global,
        IEnumerable<SettingsGroup>? groups,
        UserSettingsOverride? userOverride)
    {
        var levels = new List<Settings?> { global };

        foreach (var group in SettingsResolver.InLayerOrder(groups ?? []))
        {
            levels.Add(ReadLevel(group.SettingsJson, $"group {group.Name}"));
        }

        if (userOverride is not null)
        {
            levels.Add(ReadLevel(userOverride.SettingsJson, $"user {userOverride.UserId}"));
        }

        return SettingsResolver.Resolve([.. levels]);
    }

    /// <summary>
    /// The configuration as one caller should receive it.
    /// </summary>
    /// <param name="config">The server's configuration.</param>
    /// <param name="groups">The caller's groups, least specific first.</param>
    /// <param name="userOverride">Anything targeted at the caller alone.</param>
    /// <param name="isElevated">Whether the caller administers this server.</param>
    /// <returns>
    /// For an administrator, the configuration untouched: they have to see and edit
    /// every part of it. For anyone else, the settings resolved for them with the
    /// credentials removed, and nothing else.
    /// </returns>
    /// <remarks>
    /// This is the fix for the finding at the top of
    /// <c>docs/rewrite/state-of-the-plugin.md</c>: <c>GET config</c> handed the whole
    /// configuration, Seerr admin key included, to every account on the server.
    ///
    /// <para>
    /// The notification configuration and the admin's landing page go with it. They are
    /// server side settings, they cannot be resolved for a caller because they are not
    /// per user, and the app reads neither: it takes <c>data.settings</c> and nothing
    /// else. Serving a user the list of accounts that receive notifications is the same
    /// kind of leak as serving them the key, just quieter.
    /// </para>
    /// </remarks>
    public Config ForCaller(
        Config? config,
        IEnumerable<SettingsGroup>? groups,
        UserSettingsOverride? userOverride,
        bool isElevated)
    {
        if (isElevated)
        {
            return config ?? new Config();
        }

        return new Config
        {
            settings = SettingsResolver.Redact(Resolve(config?.settings, groups, userOverride))
        };
    }

    /// <summary>
    /// Reads one stored level, tolerating a level that cannot be read.
    /// </summary>
    /// <param name="json">The stored JSON.</param>
    /// <param name="what">What it belongs to, for the log line.</param>
    /// <returns>The settings, or <c>null</c> when there are none or they are unreadable.</returns>
    /// <remarks>
    /// Every path that reads a stored level goes through here, resolution and the
    /// administration API alike. Nothing validates the JSON on the way in, so a row
    /// edited outside the plugin, or a partial write, can leave one that does not
    /// parse. Throwing would cost far more than the level itself: on the resolution
    /// path it would break the settings endpoint for everyone in the group, and on
    /// the administration path it would make <c>GET groups</c> fail as a whole, so an
    /// administrator could no longer list or repair the group that caused it.
    /// </remarks>
    public Settings? ReadLevel(string? json, string what)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return _serialization.DeserializeJson<Settings>(json);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger?.LogWarning(
                ex,
                "Could not read the settings stored for {What}. That level was skipped",
                what);
            return null;
        }
    }
}
