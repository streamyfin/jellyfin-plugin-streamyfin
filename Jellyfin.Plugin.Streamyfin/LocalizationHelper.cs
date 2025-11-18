#pragma warning disable CA1869

using System.Globalization;
using System.Resources;
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

    public LocalizationHelper(
        ILoggerFactory? loggerFactory,
        IServerConfigurationManager? serverConfig)
    {
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
    public string GetString(string key, CultureInfo? cultureInfo = null) => 
        _resourceManager.GetString(key, cultureInfo ?? GetServerCultureInfo()) ?? key;

    private CultureInfo? GetServerCultureInfo() {
        _logger?.LogDebug("Current Server UI Culture: {0}", _serverConfig.Configuration.UICulture);
        return _serverConfig?.Configuration.UICulture != null
            ? CultureInfo.CreateSpecificCulture(_serverConfig.Configuration.UICulture.Replace("\"", ""))
            : null;
    }

    /// <summary>
    /// Get a string resource that requires string formatting
    /// </summary>
    /// <param name="key"></param>
    /// <param name="cultureInfo"></param>
    /// <param name="args"></param>
    /// <returns></returns>
    public string GetFormatted(string key, CultureInfo? cultureInfo = null, params object[] args) {
        var culture = cultureInfo ?? GetServerCultureInfo();
        
        var resource = _resourceManager.GetString(key, culture);
        return resource == null ? key : string.Format(culture, resource, args);
    }
}