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

    /// <summary>
    /// The block is a copy, not the same objects. The resolver carries each level's own
    /// Lockable into what it hands back, and the first level is the plugin's live
    /// configuration, so a block sharing those objects would be a second handle on what
    /// the server holds.
    /// </summary>
    [Fact]
    public void TheBlockIsACopy()
    {
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "http://seerr.example", locked = true }
        };

        IntegrationBlocks.Project(settings);

        settings.seerr!.serverUrl!.value = "http://somewhere.else";
        settings.seerr.serverUrl.locked = false;

        Assert.Equal("http://seerr.example", settings.jellyseerrServerUrl?.value);
        Assert.True(settings.jellyseerrServerUrl?.locked);
    }

    /// <summary>
    /// What a plain user receives carries the block too, since the app runs as one and
    /// the block exists for it. Measured before this: the redaction rebuilds the settings
    /// from the described ones, and the block is not one, so it was dropped for everybody
    /// but an administrator and the app would never have seen it.
    /// </summary>
    [Fact]
    public void WhatAPlainUserReceivesCarriesTheBlock()
    {
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "http://seerr.example" },
            autoLoginJellyseerr = new Lockable<bool> { value = true }
        };

        var redacted = SettingsResolver.Redact(settings);

        Assert.Equal("http://seerr.example", redacted.seerr?.serverUrl?.value);
        Assert.True(redacted.seerr?.autoLogin?.value);
    }

    /// <summary>
    /// And it never carries what the redaction took out. The key grants full Seerr admin
    /// access, which is the whole reason the flat one is marked secret.
    /// </summary>
    [Fact]
    public void TheBlockNeverCarriesWhatWasRedacted()
    {
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "http://seerr.example" },
            jellyseerrApiKey = new Lockable<string> { value = "SECRET-ADMIN-KEY" }
        };

        var redacted = SettingsResolver.Redact(settings);

        Assert.Null(redacted.jellyseerrApiKey);
        Assert.Null(redacted.seerr?.apiKey);
        Assert.Equal("http://seerr.example", redacted.seerr?.serverUrl?.value);
    }

    /// <summary>
    /// Nothing and an empty address are different things, and reading them as the same
    /// let a disagreement through: the block then won, quietly, on a document the plugin
    /// said it had accepted.
    /// </summary>
    [Fact]
    public void NothingIsNotAnEmptyString()
    {
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = null! },
            seerr = new SeerrSettings { serverUrl = new Lockable<string> { value = string.Empty } }
        };

        Assert.NotNull(IntegrationBlocks.Disagreement(settings));
    }

    /// <summary>
    /// A lock is part of what a setting says, so two shapes that agree on the value and
    /// not on the lock disagree.
    /// </summary>
    [Fact]
    public void TheLockIsPartOfWhatItSays()
    {
        var settings = new Settings
        {
            autoLoginJellyseerr = new Lockable<bool> { value = true, locked = false },
            seerr = new SeerrSettings { autoLogin = new Lockable<bool> { value = true, locked = true } }
        };

        Assert.NotNull(IntegrationBlocks.Disagreement(settings));
    }
}
