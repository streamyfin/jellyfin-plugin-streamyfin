using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Seerr written as a block rather than as three flat keys (P6.1).
/// </summary>
/// <remarks>
/// The keys still spell jellyseerr because every copy of the app in the field reads that
/// name, and a plugin that wrote the other spelling would take Seerr away from everyone
/// who had not updated. #159 made the new spelling readable; this makes the shape
/// readable too, and serves both, so the app can move when it is ready and the flat keys
/// come out the day it has.
/// </remarks>
public class SeerrBlockTests
{
    private readonly SerializationHelper _serialization = new();

    /// <summary>
    /// A block written by an administrator lands on the keys everything else reads.
    /// </summary>
    [Fact]
    public void ABlockLandsOnTheKeysEverythingElseReads()
    {
        var settings = _serialization.Deserialize<Settings>("""
            seerr:
              serverUrl:
                value: http://seerr.example
              apiKey:
                value: a-key
              autoLogin:
                value: true
            """);

        IntegrationBlocks.Fold(settings);

        Assert.Equal("http://seerr.example", settings.jellyseerrServerUrl?.value);
        Assert.Equal("a-key", settings.jellyseerrApiKey?.value);
        Assert.True(settings.autoLoginJellyseerr?.value);
    }

    /// <summary>
    /// A lock written on the block is the same lock.
    /// </summary>
    [Fact]
    public void ALockOnTheBlockIsTheSameLock()
    {
        var settings = new Settings
        {
            seerr = new SeerrSettings { serverUrl = new Lockable<string> { value = "http://seerr.example", locked = true } }
        };

        IntegrationBlocks.Fold(settings);

        Assert.True(settings.jellyseerrServerUrl?.locked);
    }

    /// <summary>
    /// The old spelling still wins nothing and loses nothing: a document that writes only
    /// the flat key is untouched.
    /// </summary>
    [Fact]
    public void TheFlatKeysStillWork()
    {
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "http://old.example" }
        };

        IntegrationBlocks.Fold(settings);

        Assert.Equal("http://old.example", settings.jellyseerrServerUrl?.value);
    }

    /// <summary>
    /// Both shapes saying the same thing is not a mistake.
    /// </summary>
    [Fact]
    public void BothShapesAgreeingIsFine()
    {
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "http://seerr.example" },
            seerr = new SeerrSettings { serverUrl = new Lockable<string> { value = "http://seerr.example" } }
        };

        Assert.Null(IntegrationBlocks.Disagreement(settings));
    }

    /// <summary>
    /// Both shapes saying different things is refused, rather than one of them silently
    /// winning.
    /// </summary>
    [Fact]
    public void BothShapesDisagreeingIsRefused()
    {
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "http://one.example" },
            seerr = new SeerrSettings { serverUrl = new Lockable<string> { value = "http://two.example" } }
        };

        var problem = IntegrationBlocks.Disagreement(settings);

        Assert.NotNull(problem);
        Assert.Contains("seerr.serverUrl", problem, System.StringComparison.Ordinal);
        Assert.Contains("jellyseerrServerUrl", problem, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// What is served carries both shapes, so an app reading either finds Seerr.
    /// </summary>
    [Fact]
    public void WhatIsServedCarriesBothShapes()
    {
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "http://seerr.example", locked = true },
            autoLoginJellyseerr = new Lockable<bool> { value = false }
        };

        IntegrationBlocks.Project(settings);

        Assert.Equal("http://seerr.example", settings.seerr?.serverUrl?.value);
        Assert.True(settings.seerr?.serverUrl?.locked);
        Assert.False(settings.seerr?.autoLogin?.value);
        Assert.Null(settings.seerr?.apiKey);
    }

    /// <summary>
    /// A server that says nothing about Seerr serves no block, rather than an empty one
    /// that reads as an opinion.
    /// </summary>
    [Fact]
    public void NothingSaidServesNoBlock()
    {
        var settings = new Settings();

        IntegrationBlocks.Project(settings);

        Assert.Null(settings.seerr);
    }

    /// <summary>
    /// The block is a shape, not a setting: it has no place in the schema, so it is not
    /// drawn as a field, not resolved on its own, and not something a level can override
    /// beside the key it mirrors.
    /// </summary>
    [Fact]
    public void TheBlockIsNotASettingOfItsOwn()
    {
        Assert.Null(SettingsSchema.Find("seerr"));
        Assert.DoesNotContain(SettingsSchema.Descriptors, descriptor => descriptor.Key == "seerr");
    }
}
