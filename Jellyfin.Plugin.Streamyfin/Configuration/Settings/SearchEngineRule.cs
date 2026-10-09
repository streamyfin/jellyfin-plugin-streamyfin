using System;
using Jellyfin.Plugin.Streamyfin.Configuration;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// The search engine a user ends up with (P6.4).
/// </summary>
/// <remarks>
/// <para>
/// The app carried this rule, and carried it the wrong way round: a Streamystats URL set
/// in the plugin forced <c>searchEngine</c> to Streamystats on every refresh, so an
/// administrator who had chosen Jellyfin search got it changed back under them, and
/// nothing anywhere said so. It is in <c>pluginRefreshOverlay</c> in the app, and comes
/// out once every server serves this.
/// </para>
/// <para>
/// The rule stated here instead is the one that cannot surprise anybody: the
/// administrator picks the engine, and an engine that needs a server it has not been
/// given is not served. Searching nothing is worse than searching Jellyfin, which is the
/// one engine every server has.
/// </para>
/// <para>
/// Applied after the levels resolve rather than refused on the way in, because the engine
/// and the address it needs can come from different levels: a group may choose
/// Streamystats while the server holds its address, and neither level is wrong on its
/// own.
/// </para>
/// </remarks>
public static class SearchEngineRule
{
    /// <summary>
    /// Puts the search engine back to Jellyfin when the one chosen has no server.
    /// </summary>
    /// <param name="settings">The resolved settings, changed in place.</param>
    public static void Apply(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.searchEngine is not { } chosen)
        {
            // Nothing said stays nothing said: the app has its own default and this is
            // not the place to invent one.
            return;
        }

        var served = chosen.value switch
        {
            SearchEngine.Streamystats => Reachable(settings.streamyStatsServerUrl),
            SearchEngine.Marlin => Reachable(settings.marlinServerUrl),
            _ => true
        };

        if (!served)
        {
            // A new one rather than the value on the one that was handed in. The resolver
            // builds a new Settings but carries each level's own Lockable into it, and
            // the first level is the plugin's live configuration: setting the value here
            // reached into what the server holds, so one request without a Streamystats
            // URL turned the administrator's choice into Jellyfin for everybody, and the
            // next save wrote it down.
            //
            // The lock is the administrator's and stays theirs. The fallback is not a
            // choice anybody made, so it does not unlock what they locked.
            settings.searchEngine = new Lockable<SearchEngine>
            {
                value = SearchEngine.Jellyfin,
                locked = chosen.locked
            };
        }
    }

    private static bool Reachable(Lockable<string>? address) =>
        !string.IsNullOrWhiteSpace(address?.value);
}
