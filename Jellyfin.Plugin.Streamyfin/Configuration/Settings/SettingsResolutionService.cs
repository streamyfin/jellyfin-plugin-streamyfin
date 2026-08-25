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
            levels.Add(Read(group.SettingsJson, $"group {group.Name}"));
        }

        if (userOverride is not null)
        {
            levels.Add(Read(userOverride.SettingsJson, $"user {userOverride.UserId}"));
        }

        return SettingsResolver.Resolve([.. levels]);
    }

    private Settings? Read(string json, string what)
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
