using System;
using System.Collections.Concurrent;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;

public abstract class BaseEvent
{
    private static readonly ConcurrentDictionary<string, DateTime> RecentEvents = new();
    private static readonly TimeSpan RecentEventThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CleanupThreshold = TimeSpan.FromMinutes(5);

    protected static Config? Config => StreamyfinPlugin.Instance?.Configuration.Config;
    
    protected readonly ILogger _logger;
    protected readonly LocalizationHelper _localization;
    protected readonly IServerApplicationHost _applicationHost;
    protected readonly NotificationHelper _notificationHelper;

    protected BaseEvent(
        ILoggerFactory loggerFactory,
        LocalizationHelper localization,
        IServerApplicationHost applicationHost,
        NotificationHelper notificationHelper)
    {
        _logger = loggerFactory.CreateLogger(GetType());
        _localization = localization;
        _applicationHost = applicationHost;
        _notificationHelper = notificationHelper;
    }

    /// <summary>
    /// Check if the event was recently processed before
    /// </summary>
    /// <param name="sessionKey"></param>
    /// <returns></returns>
    protected bool HasRecentlyProcessed(string sessionKey)
    {
        _logger.LogDebug("Checking recent events for key: {0}", sessionKey);

        var recentlyProcessed = 
            RecentEvents.TryGetValue(sessionKey, out DateTime lastProcessedTime) && 
            DateTime.UtcNow - lastProcessedTime < GetRecentEventThreshold();

        if (!recentlyProcessed)
        {
            _logger.LogDebug("No recent events for key: {0}", sessionKey);
            // Update the cache with the latest event time
            RecentEvents[sessionKey] = DateTime.UtcNow;
        }
        else _logger.LogDebug("There are recent events for key: {0}", sessionKey);

        return recentlyProcessed;
    }
    
    /// <summary>
    /// Cleans up old session entries from the cache.
    /// </summary>
    protected void CleanupOldEntries()
    {
        DateTime threshold = DateTime.UtcNow - GetCleanupThreshold();

        _logger.LogDebug("Checking for entries older than: {0}", threshold);

        RecentEvents
            .Where(kvp => kvp.Value < threshold)
            .Select(kvp => kvp.Key)
            .ToList()
            .ForEach(key => RecentEvents.TryRemove(key, out _));
    }

    /// <summary>
    /// How long we want to wait until allowing an event with a matching sessionKey to be processed
    /// </summary>
    /// <returns>TimeSpan for how long to wait</returns>
    protected virtual TimeSpan GetRecentEventThreshold()
    {
        _logger.LogDebug("Getting default RecentEventsThreshold: {0}", RecentEventThreshold);
        return RecentEventThreshold;
    }

    /// <summary>
    /// Maximum age we want events stored our recentEvents cache to be
    /// </summary>
    /// <returns>TimeSpan for how long to wait</returns>
    protected virtual TimeSpan GetCleanupThreshold()
    {
        return GetRecentEventThreshold() + CleanupThreshold;
    }
}