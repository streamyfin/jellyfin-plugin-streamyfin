using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The search engine a user ends up with (P6.4).
/// </summary>
/// <remarks>
/// The app carried this rule and carried it the wrong way round: setting a Streamystats
/// URL in the plugin forced <c>searchEngine</c> to Streamystats on every refresh, so an
/// administrator who had chosen Jellyfin search got it changed back under them, and
/// nothing anywhere said so.
///
/// The rule the plugin states instead is the one that cannot surprise anybody: the
/// administrator picks the engine, and an engine that needs a server it has not been
/// given is not served. Searching nothing is worse than searching Jellyfin.
/// </remarks>
public class SearchEngineRuleTests
{
    private static Settings With(SearchEngine? engine, string? streamystats = null, string? marlin = null)
    {
        var settings = new Settings();

        if (engine is { } chosen)
        {
            settings.searchEngine = new Lockable<SearchEngine> { value = chosen };
        }

        if (streamystats is not null)
        {
            settings.streamyStatsServerUrl = new Lockable<string> { value = streamystats };
        }

        if (marlin is not null)
        {
            settings.marlinServerUrl = new Lockable<string> { value = marlin };
        }

        return settings;
    }

    /// <summary>
    /// An engine with the server it needs is left alone.
    /// </summary>
    [Fact]
    public void AnEngineWithItsServerIsLeftAlone()
    {
        var streamystats = With(SearchEngine.Streamystats, streamystats: "http://stats.example");
        SearchEngineRule.Apply(streamystats);
        Assert.Equal(SearchEngine.Streamystats, streamystats.searchEngine!.value);

        var marlin = With(SearchEngine.Marlin, marlin: "http://marlin.example");
        SearchEngineRule.Apply(marlin);
        Assert.Equal(SearchEngine.Marlin, marlin.searchEngine!.value);
    }

    /// <summary>
    /// An engine without the server it needs searches Jellyfin, which is the one engine
    /// every server has.
    /// </summary>
    [Theory]
    [InlineData(SearchEngine.Streamystats)]
    [InlineData(SearchEngine.Marlin)]
    public void AnEngineWithoutItsServerFallsBackToJellyfin(SearchEngine engine)
    {
        var settings = With(engine);

        SearchEngineRule.Apply(settings);

        Assert.Equal(SearchEngine.Jellyfin, settings.searchEngine!.value);
    }

    /// <summary>
    /// A URL that is only spaces is not a server, which is what an emptied box leaves
    /// behind.
    /// </summary>
    [Fact]
    public void AnAddressOfSpacesIsNoServer()
    {
        var settings = With(SearchEngine.Streamystats, streamystats: "   ");

        SearchEngineRule.Apply(settings);

        Assert.Equal(SearchEngine.Jellyfin, settings.searchEngine!.value);
    }

    /// <summary>
    /// Whether the engine was locked survives being changed, since the lock is the
    /// administrator's and the fallback is not a choice anybody made.
    /// </summary>
    [Fact]
    public void TheLockSurvives()
    {
        var settings = With(SearchEngine.Streamystats);
        settings.searchEngine!.locked = true;

        SearchEngineRule.Apply(settings);

        Assert.Equal(SearchEngine.Jellyfin, settings.searchEngine.value);
        Assert.True(settings.searchEngine.locked);
    }

    /// <summary>
    /// Nothing said about the engine stays nothing said: the app has its own default and
    /// this is not the place to invent one.
    /// </summary>
    [Fact]
    public void NothingSaidStaysNothingSaid()
    {
        var settings = With(null, streamystats: "http://stats.example");

        SearchEngineRule.Apply(settings);

        Assert.Null(settings.searchEngine);
    }

    /// <summary>
    /// Jellyfin needs no server of its own.
    /// </summary>
    [Fact]
    public void JellyfinNeedsNothing()
    {
        var settings = With(SearchEngine.Jellyfin);

        SearchEngineRule.Apply(settings);

        Assert.Equal(SearchEngine.Jellyfin, settings.searchEngine!.value);
    }

    /// <summary>
    /// The rule is applied where the app reads its settings, and reads across the levels:
    /// a group may choose the engine while the server holds the address, and that pair is
    /// served rather than undone.
    /// </summary>
    [Fact]
    public void TheAddressMayComeFromAnotherLevel()
    {
        var directory = TestDirectory.Create();

        try
        {
            var db = new Jellyfin.Plugin.Streamyfin.Db.PluginDatabase(directory);
            var serialization = new SerializationHelper();
            var resolution = new SettingsResolutionService(serialization);
            var userId = System.Guid.NewGuid();

            var group = db.SaveSettingsGroup(new Jellyfin.Plugin.Streamyfin.Db.SettingsGroup
            {
                Name = "Searchers",
                Priority = 1,
                SettingsJson = serialization.SerializeToJson(With(SearchEngine.Streamystats))
            });

            db.SetGroupMembers(group.Id, [userId]);

            var server = With(null, streamystats: "http://stats.example");

            var resolved = resolution.Resolve(server, db.GetGroupsForUser(userId), db.GetUserSettingsOverride(userId));

            Assert.Equal(SearchEngine.Streamystats, resolved.searchEngine!.value);

            var withoutTheAddress = resolution.Resolve(new Settings(), db.GetGroupsForUser(userId), db.GetUserSettingsOverride(userId));

            Assert.Equal(SearchEngine.Jellyfin, withoutTheAddress.searchEngine!.value);
        }
        finally
        {
            TestDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// The fallback changes what one caller is served, not what the server holds.
    /// </summary>
    /// <remarks>
    /// The resolver builds a new Settings but carries the levels' own Lockable objects
    /// into it, and the first level is the plugin's live configuration. Setting the value
    /// on the one it handed back therefore reached into the stored configuration: one
    /// request without a Streamystats URL would have turned the administrator's choice
    /// into Jellyfin for everybody, and the next save would have written it down.
    /// </remarks>
    [Fact]
    public void TheServerKeepsWhatTheAdministratorChose()
    {
        var directory = TestDirectory.Create();

        try
        {
            var db = new Jellyfin.Plugin.Streamyfin.Db.PluginDatabase(directory);
            var resolution = new SettingsResolutionService(new SerializationHelper());
            var global = With(SearchEngine.Streamystats);

            var served = resolution.Resolve(global, null, null);

            Assert.Equal(SearchEngine.Jellyfin, served.searchEngine!.value);
            Assert.Equal(SearchEngine.Streamystats, global.searchEngine!.value);
        }
        finally
        {
            TestDirectory.Delete(directory);
        }
    }
}
