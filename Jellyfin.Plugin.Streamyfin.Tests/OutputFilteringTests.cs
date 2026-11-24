using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Jellyfin.Plugin.Streamyfin.Db;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What each caller is allowed to receive from the configuration endpoints.
///
/// This is the fix for the finding at the top of
/// <c>docs/rewrite/state-of-the-plugin.md</c>: <c>GET config</c> handed the whole
/// configuration, Seerr admin key included, to every account on the server.
/// </summary>
public class OutputFilteringTests
{
    private readonly SerializationHelper _serialization = new();
    private readonly SettingsResolutionService _resolution;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputFilteringTests"/> class.
    /// </summary>
    public OutputFilteringTests()
    {
        _resolution = new SettingsResolutionService(_serialization);
    }

    private static Config ServerConfig() => new()
    {
        settings = new Settings
        {
            subtitleSize = new Lockable<int> { locked = true, value = 80 },
            jellyseerrApiKey = new Lockable<string> { locked = true, value = "a-real-key" },
            jellyseerrServerUrl = new Lockable<string> { locked = true, value = "https://seerr.example" }
        },
        notifications = new Notifications
        {
            ItemAdded = new ItemAddedNotificationConfiguration
            {
                Enabled = true,
                EnabledLibraries = ["a-library-id"]
            }
        },
        Other = new Other { HomePage = "Yaml" }
    };

    /// <summary>
    /// An administrator receives the configuration untouched. They have to see and edit
    /// every part of it, credentials included, or they cannot administer anything.
    /// </summary>
    [Fact]
    public void AnAdministratorSeesEverything()
    {
        var config = ServerConfig();

        var served = _resolution.ForCaller(config, null, null, isElevated: true);

        Assert.Same(config, served);
        Assert.Equal("a-real-key", served.settings?.jellyseerrApiKey?.value);
        Assert.NotNull(served.notifications);
        Assert.Equal("Yaml", served.Other?.HomePage);
    }

    /// <summary>
    /// Everyone else loses the credentials. This is the whole point of the part.
    /// </summary>
    [Fact]
    public void EveryoneElseLosesTheCredentials()
    {
        var served = _resolution.ForCaller(ServerConfig(), null, null, isElevated: false);

        Assert.Null(served.settings?.jellyseerrApiKey);
        Assert.Equal("https://seerr.example", served.settings?.jellyseerrServerUrl?.value);
    }

    /// <summary>
    /// And the server side blocks, which are not per user and which the app does not
    /// read. It takes <c>data.settings</c> and nothing else. Serving a user the list of
    /// accounts that receive notifications is the same kind of leak as the key, quieter.
    /// </summary>
    [Fact]
    public void EveryoneElseLosesTheServerSideBlocks()
    {
        var served = _resolution.ForCaller(ServerConfig(), null, null, isElevated: false);

        Assert.Null(served.notifications);
        Assert.Null(served.Other);
        Assert.NotNull(served.settings);
    }

    /// <summary>
    /// A caller's own levels are applied on the way out, so what they receive is what
    /// applies to them rather than what applies to everyone.
    /// </summary>
    [Fact]
    public void ACallersLevelsAreResolvedOnTheWayOut()
    {
        var group = new SettingsGroup
        {
            Name = "Staff",
            SettingsJson = _serialization.SerializeToJson(new Settings
            {
                subtitleSize = new Lockable<int> { locked = false, value = 60 }
            })
        };

        var served = _resolution.ForCaller(ServerConfig(), [group], null, isElevated: false);

        Assert.Equal(60, served.settings?.subtitleSize?.value);
        Assert.False(served.settings?.subtitleSize?.locked);
    }

    /// <summary>
    /// A credential a group tried to hand out is still removed. Redaction is the last
    /// thing that happens, so no level can put a key back.
    /// </summary>
    [Fact]
    public void AGroupCannotHandOutACredential()
    {
        var group = new SettingsGroup
        {
            Name = "Trusted",
            SettingsJson = _serialization.SerializeToJson(new Settings
            {
                jellyseerrApiKey = new Lockable<string> { locked = false, value = "handed-out" }
            })
        };

        var served = _resolution.ForCaller(ServerConfig(), [group], null, isElevated: false);

        Assert.Null(served.settings?.jellyseerrApiKey);
    }

    /// <summary>
    /// An administrator gets the raw configuration rather than their own resolved view,
    /// because the admin pages edit what the server declares. Serving them a resolved set
    /// would mean saving it back and writing their group's overrides into the global
    /// configuration.
    /// </summary>
    [Fact]
    public void AnAdministratorIsNotServedTheirOwnResolvedView()
    {
        var group = new SettingsGroup
        {
            Name = "Staff",
            SettingsJson = _serialization.SerializeToJson(new Settings
            {
                subtitleSize = new Lockable<int> { locked = false, value = 60 }
            })
        };

        var served = _resolution.ForCaller(ServerConfig(), [group], null, isElevated: true);

        Assert.Equal(80, served.settings?.subtitleSize?.value);
    }

    /// <summary>
    /// A server with no configuration at all serves an empty one rather than throwing.
    /// </summary>
    [Fact]
    public void NoConfigurationIsNotAFailure()
    {
        Assert.NotNull(_resolution.ForCaller(null, null, null, isElevated: true));
        Assert.NotNull(_resolution.ForCaller(null, null, null, isElevated: false));
    }
}
