#pragma warning disable CA1869

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.Logging;


namespace Jellyfin.Plugin.Streamyfin;

/// <summary>
/// Serialization settings for json and yaml
/// </summary>
public class LocalizationHelper
{
    protected readonly ILogger? _logger;
    private readonly IServerConfigurationManager? _serverConfig;
    private readonly ResourceManager _resourceManager;
    private readonly Func<IReadOnlyList<WordingOverride>?> _wording;

    public LocalizationHelper(
        ILoggerFactory? loggerFactory,
        IServerConfigurationManager? serverConfig)
        : this(loggerFactory, serverConfig, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizationHelper"/> class with its
    /// own source of wording, which is what makes #34 testable without a plugin instance.
    /// </summary>
    /// <param name="loggerFactory">Where to log.</param>
    /// <param name="serverConfig">The server's own configuration, for its language.</param>
    /// <param name="wording">
    /// What an administrator says instead of the plugin's own sentences, read each time
    /// rather than held, since the configuration is edited while the server runs.
    /// </param>
    public LocalizationHelper(
        ILoggerFactory? loggerFactory,
        IServerConfigurationManager? serverConfig,
        Func<IReadOnlyList<WordingOverride>?>? wording)
    {
        _wording = wording ?? (() => StreamyfinPlugin.Instance?.Configuration.Config.notifications?.Wording);

        if (loggerFactory != null)
        {
            _logger = loggerFactory.CreateLogger<LocalizationHelper>();
        }

        _serverConfig = serverConfig;
        _resourceManager = new ResourceManager(
            baseName: "Jellyfin.Plugin.Streamyfin.Resources.Strings",
            assembly: typeof(LocalizationHelper).Assembly
        );
    }

    /// <summary>
    /// Get string resource or fallback to key to avoid nullable strings
    /// </summary>
    /// <param name="key"></param>
    /// <param name="cultureInfo"></param>
    /// <returns></returns>
    public string GetString(string key, CultureInfo? cultureInfo = null)
    {
        var culture = cultureInfo ?? GetServerCultureInfo();

        return Said(key, culture) ?? _resourceManager.GetString(key, culture) ?? key;
    }

    /// <summary>
    /// Falls back to the invariant culture rather than to the ambient one.
    /// Returning null hands the choice to CultureInfo.CurrentUICulture, so the
    /// strings a server produced depended on the locale of the machine it ran
    /// on instead of on its configured UICulture.
    /// </summary>
    private CultureInfo GetServerCultureInfo() {
        _logger?.LogInformation("Current Server UI Culture: {0}", _serverConfig?.Configuration.UICulture);
        return _serverConfig?.Configuration.UICulture != null
            ? CultureInfo.CreateSpecificCulture(_serverConfig.Configuration.UICulture.Replace("\"", "", StringComparison.Ordinal))
            : CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// Get a string resource that requires string formatting
    /// </summary>
    /// <param name="key"></param>
    /// <param name="cultureInfo"></param>
    /// <param name="args"></param>
    /// <returns></returns>
    public string GetFormatted(string key, CultureInfo? cultureInfo = null, params object?[] args) {
        var culture = cultureInfo ?? GetServerCultureInfo();
        
        var resource = _resourceManager.GetString(key, culture);
        var said = Said(key, culture);

        if (said is not null)
        {
            try
            {
                return string.Format(culture, said, args);
            }
            catch (FormatException thrown)
            {
                // A sentence asking for something the event does not carry would throw
                // inside an event handler the server is waiting on. The plugin's own
                // wording answers instead, and the log says why.
                _logger?.LogWarning(
                    thrown,
                    "The wording set for {Key} asks for something the event does not carry, so the plugin's own was used",
                    key);
            }
        }

        return resource == null ? key : string.Format(culture, resource, args);
    }

    // What an administrator says instead, for this sentence and this language.
    private string? Said(string key, CultureInfo culture)
    {
        try
        {
            return Configuration.Notifications.Wording.For(key, culture, _wording());
        }
        catch (Exception thrown)
        {
            // Reading the configuration must never be what stops a notification.
            _logger?.LogWarning(thrown, "Could not read the wording set for {Key}", key);
            return null;
        }
    }
}