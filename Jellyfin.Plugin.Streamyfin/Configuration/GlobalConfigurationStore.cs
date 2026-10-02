using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Jellyfin.Plugin.Streamyfin.Db;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.Configuration;

/// <summary>
/// The configuration the server declares for everyone, and the only way to read or
/// write it.
/// </summary>
/// <remarks>
/// Named for the level it holds rather than simply <c>ConfigurationStore</c>, which
/// Jellyfin already has in <c>MediaBrowser.Common.Configuration</c> and which means
/// something else entirely.
/// </remarks>
/// <remarks>
/// It lived in Jellyfin's plugin configuration XML until P1.5, which left the three
/// targeting levels in two stores: the global one in a file the server owned, groups
/// and per user overrides in the plugin's own database. Anything reasoning about all
/// three had to know about both, and only one of them could be read inside a
/// transaction.
///
/// <para>
/// The XML is imported once and never written to or deleted, so a downgrade to a build
/// that still reads it finds it exactly as it was left, which is the same rollback path
/// the device token import took in P0.4. It is still read on every start: what that
/// build changes in it is named in the log on the way back up, see
/// <see cref="LegacyFileChanges"/>.
/// </para>
/// </remarks>
public sealed class GlobalConfigurationStore
{
    /// <summary>
    /// The file Jellyfin writes a plugin's configuration to, named after the assembly.
    /// </summary>
    internal const string LegacyFileName = "Jellyfin.Plugin.Streamyfin.xml";

    private readonly PluginDatabase _database;
    private readonly SerializationHelper _serialization;
    private readonly ILogger<GlobalConfigurationStore>? _logger;
    private readonly object _gate = new();

    private Config? _cached;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalConfigurationStore"/> class.
    /// </summary>
    /// <param name="database">The plugin's database.</param>
    /// <param name="serialization">The plugin's serializer.</param>
    /// <param name="logger">Optional logger.</param>
    public GlobalConfigurationStore(
        PluginDatabase database,
        SerializationHelper serialization,
        ILogger<GlobalConfigurationStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(serialization);

        _database = database;
        _serialization = serialization;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current configuration.
    /// </summary>
    /// <remarks>
    /// Held in memory between writes. A playback event reads this, and going to SQLite
    /// on every notification to deserialise a document that changes a few times a year
    /// would be a poor trade.
    /// </remarks>
    public Config Current
    {
        get
        {
            lock (_gate)
            {
                return _cached ??= Read();
            }
        }
    }

    /// <summary>
    /// Replaces the configuration.
    /// </summary>
    /// <param name="config">The new configuration.</param>
    public void Save(Config config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_gate)
        {
            _database.SaveGlobalConfigJson(_serialization.SerializeToJson(config));
            _cached = config;
        }
    }

    /// <summary>
    /// Carries the configuration over from Jellyfin's plugin XML, once, and on every
    /// later start names what an earlier version of the plugin changed in it.
    /// </summary>
    /// <param name="legacy">The configuration as Jellyfin deserialized it.</param>
    /// <param name="pluginConfigurationsPath">
    /// Where Jellyfin keeps plugin configuration files, used only to report the keys it
    /// dropped. Pass <c>null</c> to skip that.
    /// </param>
    public void Import(Config? legacy, string? pluginConfigurationsPath)
    {
        var json = _serialization.SerializeToJson(legacy ?? new Config());

        if (!_database.ImportGlobalConfiguration(json))
        {
            ReportLaterChanges(json);
            return;
        }

        lock (_gate)
        {
            _cached = null;
        }

        _logger?.LogInformation(
            "Imported the configuration from {File}. The file is left in place and never written; "
            + "a setting an earlier version of the plugin changes in it is named at the next start",
            LegacyFileName);

        ReportKeysJellyfinDropped(pluginConfigurationsPath);
    }

    /// <summary>
    /// Names what an earlier version of the plugin changed in the old file since this
    /// version last read it.
    /// </summary>
    /// <param name="fileJson">The file's configuration as it stands, as JSON.</param>
    /// <remarks>
    /// Runs during plugin activation, so a failure is logged and the next start tries
    /// again, with the copy of the file left as it was.
    /// </remarks>
    private void ReportLaterChanges(string fileJson)
    {
        try
        {
            var lastRead = _database.GetLegacyFileJson();

            if (lastRead is null)
            {
                // Imported by a build that kept no copy. A change made before this start
                // cannot be told apart from what was imported, so this start is the
                // reference and later changes are the ones named.
                _database.SaveLegacyFileJson(fileJson);
                return;
            }

            var before = Normalized(lastRead);
            var now = Normalized(fileJson);

            if (LegacyFileChanges.Same(before, now))
            {
                return;
            }

            if (LegacyFileChanges.Same(now, Normalized(_serialization.SerializeToJson(PluginConfiguration.DefaultConfig()))))
            {
                // Jellyfin writes a plugin's defaults over a file that is missing or that it
                // cannot read. Nobody chose those values, so they are not listed as changes.
                _logger?.LogInformation(
                    "{File} holds this version's defaults, which is what Jellyfin writes when the file is missing or unreadable",
                    LegacyFileName);
            }
            else
            {
                var entries = LegacyFileChanges.NotUsedHere(before, now, Normalized(_database.GetGlobalConfigJson()));

                if (entries.Count > 0)
                {
                    // Names only: a setting can be a credential. An older version fills a
                    // missing file with its own defaults, which cannot be told apart from
                    // choices, hence the second half of the message.
                    _logger?.LogWarning(
                        "{File} was changed since this version last read it, most likely by an earlier version of the plugin. "
                        + "{Count} setting(s) there differ from the ones used here and were not applied: {Keys}. "
                        + "If they were changed on that version, set them again in the dashboard. "
                        + "If the file was deleted while it ran, these are that version's defaults and can be ignored",
                        LegacyFileName,
                        entries.Count,
                        string.Join(", ", entries));
                }
            }

            // Only once the change has been said, so a start that fails before that names it
            // on the next one. From here the file as it stands is the reference.
            _database.SaveLegacyFileJson(fileJson);
        }
        catch (Exception ex)
        {
            // Not fatal, like the device token import: a server that cannot compare the
            // file still starts with the configuration it had.
            _logger?.LogWarning(
                ex,
                "Could not compare {File} with the copy taken when it was last read. It will be compared again on the next start",
                LegacyFileName);
        }
    }

    // The same tidying every read path applies, so a section that only gained its kind
    // here, or an address that was only trimmed, is not mistaken for a change.
    private JsonObject Normalized(string? json)
    {
        var config = string.IsNullOrWhiteSpace(json)
            ? new Config()
            : _serialization.DeserializeJson<Config>(json) ?? new Config();

        Sections.Declare(config.settings);
        Sections.Sort(config.settings);
        SettingsValidation.Tidy(config.settings);

        return JsonNode.Parse(_serialization.SerializeToJson(config)) as JsonObject ?? [];
    }

    /// <summary>
    /// Finds settings in the old file that this plugin no longer declares.
    /// </summary>
    /// <param name="pluginConfigurationsPath">Where Jellyfin keeps plugin configuration files.</param>
    /// <remarks>
    /// The XML deserializer drops an element it has no property for, silently and before
    /// anything here sees it. So an administrator who set a key that was later removed or
    /// renamed has been running with a value that does nothing, and no way to find out.
    /// Reading the file directly is the only way to say so.
    ///
    /// <para>
    /// It reports rather than guesses. A removed setting has no new home to move a value
    /// into, and inventing one would be worse than saying the value is not used.
    /// </para>
    /// </remarks>
    private void ReportKeysJellyfinDropped(string? pluginConfigurationsPath)
    {
        if (string.IsNullOrWhiteSpace(pluginConfigurationsPath))
        {
            return;
        }

        var path = Path.Combine(pluginConfigurationsPath, LegacyFileName);

        if (!File.Exists(path))
        {
            return;
        }

        List<string> unknown;

        try
        {
            unknown = UnknownSettingKeys(XDocument.Load(path));
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            _logger?.LogDebug(ex, "Could not read {Path} to check for settings that are no longer used", path);
            return;
        }

        if (unknown.Count == 0)
        {
            return;
        }

        _logger?.LogWarning(
            "{Count} setting(s) in {File} are not used by this version and were not carried over: {Keys}. "
            + "They were removed or renamed in an earlier release, and the values have not been doing anything",
            unknown.Count,
            LegacyFileName,
            string.Join(", ", unknown));
    }

    /// <summary>
    /// The settings in an old configuration file that this version does not declare.
    /// </summary>
    /// <param name="document">The parsed file.</param>
    /// <returns>The key names, in the order the file lists them.</returns>
    internal static List<string> UnknownSettingKeys(XDocument document)
    {
        var settings = document?.Root?.Element("Config")?.Element("settings");

        if (settings is null)
        {
            return [];
        }

        return settings.Elements()
            .Select(element => element.Name.LocalName)
            .Where(name => SettingsSchema.Find(name) is null)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private Config Read()
    {
        var json = _database.GetGlobalConfigJson();

        if (string.IsNullOrWhiteSpace(json))
        {
            return new Config();
        }

        try
        {
            return _serialization.DeserializeJson<Config>(json) ?? new Config();
        }
        catch (System.Text.Json.JsonException ex)
        {
            // Serving an empty configuration beats refusing to start. Every setting then
            // reads as "the server has no opinion", which is the same as a fresh install.
            _logger?.LogError(ex, "The stored configuration could not be read. Serving an empty one");
            return new Config();
        }
    }
}
