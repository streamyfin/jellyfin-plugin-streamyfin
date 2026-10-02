using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    /// <param name="canOpen">
    /// Whether the caller may open a library, by id. A home section built on one they
    /// cannot is left out. <c>null</c> when there is no user to ask about, which is an
    /// API key.
    /// </param>
    /// <returns>The settings the caller receives.</returns>
    public Settings Resolve(
        Settings? global,
        IEnumerable<SettingsGroup>? groups,
        UserSettingsOverride? userOverride,
        Func<Guid, bool>? canOpen = null)
    {
        var levels = new List<Settings?> { global };

        foreach (var group in SettingsResolver.InLayerOrder(groups ?? []))
        {
            levels.Add(ReadLevel(group.SettingsJson, $"group {group.Id}"));
        }

        if (userOverride is not null)
        {
            levels.Add(ReadLevel(userOverride.SettingsJson, $"user {userOverride.UserId}"));
        }

        var resolved = SettingsResolver.Resolve([.. levels]);

        // The home sections leave here as the app has to draw them: each saying what
        // kind it is, and in the order the administrator asked for. A level that adds a
        // section can only append it, so without this the order would be whichever
        // level happened to speak last.
        Sections.Declare(resolved);
        Sections.Sort(resolved);

        // After the sort, so what is left keeps the position it has for everyone.
        if (canOpen is not null)
        {
            Sections.KeepVisible(resolved, canOpen);
        }

        // Here rather than refused on the way in, because the engine and the address it
        // needs can come from different levels: a group may choose Streamystats while the
        // server holds its address, and neither level is wrong on its own.
        SearchEngineRule.Apply(resolved);

        // Both shapes leave here, so an app reading either finds Seerr. The flat keys go
        // the day every copy in the field reads the block. A caller who is not an
        // administrator gets this again after the redaction, from what is left.
        IntegrationBlocks.Project(resolved);

        return resolved;
    }

    /// <summary>
    /// The configuration as one caller should receive it.
    /// </summary>
    /// <param name="config">The server's configuration.</param>
    /// <param name="groups">The caller's groups, least specific first.</param>
    /// <param name="userOverride">Anything targeted at the caller alone.</param>
    /// <param name="isElevated">Whether the caller administers this server.</param>
    /// <param name="canOpen">
    /// Whether the caller may open a library, by id. Only read when the caller is not
    /// elevated: an administrator is handed the configuration to edit, all of it.
    /// </param>
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
        bool isElevated,
        Func<Guid, bool>? canOpen = null)
    {
        // Every section says what kind it is by the time it leaves here, whether or not
        // the stored copy does. Nothing is written back: a GET does not rewrite the
        // database, and the kind a payload implies is the same answer every time.
        Sections.Declare(config?.settings);
        Sections.Sort(config?.settings);

        // A server that stored an empty address before one stopped being storable would
        // otherwise hand the admin page two problems it did not make, on every load,
        // until somebody saved. Tidied on the way out as well as on the way in.
        SettingsValidation.Tidy(config?.settings);

        if (isElevated)
        {
            return config ?? new Config();
        }

        return new Config
        {
            settings = SettingsResolver.Redact(Resolve(config?.settings, groups, userOverride, canOpen))
        };
    }

    /// <summary>
    /// Reads one stored level, tolerating a level that cannot be read.
    /// </summary>
    /// <param name="json">The stored JSON.</param>
    /// <param name="what">Which level it is, for the log line. An identity rather
    /// than a name: a group is named by whoever created it, and that name should not
    /// decide what a log file says.</param>
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
            var settings = _serialization.DeserializeJson<Settings>(json);
            IntegrationBlocks.Fold(settings);
            Sections.Declare(settings);
            Sections.Sort(settings);
            SettingsValidation.Tidy(settings);
            return settings;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger?.LogWarning(
                ex,
                "Could not read the settings stored for {What}. That level was skipped",
                OnOneLine(what));
            return null;
        }
    }

    // Callers name a level by its id, so nothing typed by an administrator should
    // reach this line. It is cleaned anyway: a newline in a log message forges a
    // second entry, and the whole point of this line is that it appears when
    // something has already gone wrong with stored data.
    private static string OnOneLine(string? what)
    {
        const int Longest = 120;

        if (string.IsNullOrWhiteSpace(what))
        {
            return "an unnamed level";
        }

        var cleaned = new StringBuilder(Math.Min(what.Length, Longest));

        foreach (var character in what)
        {
            if (cleaned.Length == Longest)
            {
                cleaned.Append('\u2026');
                break;
            }

            cleaned.Append(char.IsControl(character) ? ' ' : character);
        }

        return cleaned.ToString();
    }
}
