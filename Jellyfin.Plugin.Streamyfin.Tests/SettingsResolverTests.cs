using System;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Jellyfin.Plugin.Streamyfin.Db;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Precedence across the three targeting levels: what the server declares for
/// everyone, the groups an administrator defines, and anything aimed at one user.
///
/// The rule is that the most specific level which says something about a key wins,
/// and that includes the lock. Not "the most restrictive lock wins": the shape the
/// maintainers proposed in issue #29 has an override setting <c>lock: false</c> to
/// hand a setting back to named users, and a resolver that could only tighten would
/// make that impossible.
/// </summary>
public class SettingsResolverTests
{
    private static Settings Locked(int size) => new()
    {
        subtitleSize = new Lockable<int> { locked = true, value = size }
    };

    private static Settings Free(int size) => new()
    {
        subtitleSize = new Lockable<int> { locked = false, value = size }
    };

    /// <summary>
    /// A key only the server sets comes through untouched.
    /// </summary>
    [Fact]
    public void TheServerLevelAppliesWhenNothingElseSpeaks()
    {
        var resolved = SettingsResolver.Resolve(Locked(80), null, null);

        Assert.Equal(80, resolved.subtitleSize?.value);
        Assert.True(resolved.subtitleSize?.locked);
    }

    /// <summary>
    /// A group beats the server.
    /// </summary>
    [Fact]
    public void AGroupBeatsTheServer()
    {
        var resolved = SettingsResolver.Resolve(Locked(80), Locked(60));

        Assert.Equal(60, resolved.subtitleSize?.value);
    }

    /// <summary>
    /// A user beats a group, which beats the server.
    /// </summary>
    [Fact]
    public void TheUserLevelBeatsEverything()
    {
        var resolved = SettingsResolver.Resolve(Locked(80), Locked(60), Locked(40));

        Assert.Equal(40, resolved.subtitleSize?.value);
    }

    /// <summary>
    /// A more specific level can unlock what the server locked. This is the case from
    /// issue #29 and the reason precedence is by specificity rather than by
    /// restrictiveness.
    /// </summary>
    [Fact]
    public void AMoreSpecificLevelCanUnlock()
    {
        var resolved = SettingsResolver.Resolve(Locked(80), Free(80));

        Assert.False(resolved.subtitleSize?.locked);
        Assert.Equal(80, resolved.subtitleSize?.value);
    }

    /// <summary>
    /// Levels are merged per key, not wholesale. A group that speaks about one setting
    /// does not silently drop the forty others the server declared.
    /// </summary>
    [Fact]
    public void LevelsMergePerKeyRatherThanReplacing()
    {
        var global = new Settings
        {
            subtitleSize = new Lockable<int> { locked = true, value = 80 },
            forwardSkipTime = new Lockable<int> { locked = false, value = 30 }
        };

        var group = new Settings
        {
            subtitleSize = new Lockable<int> { locked = false, value = 60 }
        };

        var resolved = SettingsResolver.Resolve(global, group);

        Assert.Equal(60, resolved.subtitleSize?.value);
        Assert.Equal(30, resolved.forwardSkipTime?.value);
    }

    /// <summary>
    /// A key nobody sets stays unset, which the client reads as the server having no
    /// opinion. Filling it with a default here would take the choice away from the
    /// user without anyone deciding to.
    /// </summary>
    [Fact]
    public void AKeyNobodySetsStaysUnset()
    {
        var resolved = SettingsResolver.Resolve(Locked(80));

        Assert.Null(resolved.rewindSkipTime);
    }

    /// <summary>
    /// Absent levels are skipped rather than treated as empty ones that clear
    /// everything above them.
    /// </summary>
    [Fact]
    public void NullLevelsAreSkipped()
    {
        var resolved = SettingsResolver.Resolve(null, Locked(60), null);

        Assert.Equal(60, resolved.subtitleSize?.value);
    }

    /// <summary>
    /// Resolving nothing is an empty set rather than a crash.
    /// </summary>
    [Fact]
    public void ResolvingNothingGivesAnEmptySet()
    {
        var resolved = SettingsResolver.Resolve();

        Assert.All(
            SettingsSchema.Descriptors,
            d => Assert.Null(d.Property.GetValue(resolved)));
    }

    /// <summary>
    /// The result is a new object. Resolving must not write into the plugin's live
    /// configuration, which is a singleton every request shares.
    /// </summary>
    [Fact]
    public void ResolvingDoesNotMutateTheLevels()
    {
        var global = Locked(80);

        var resolved = SettingsResolver.Resolve(global, Locked(60));

        Assert.Equal(80, global.subtitleSize?.value);
        Assert.NotSame(global, resolved);
    }

    /// <summary>
    /// A higher priority is layered later, so it wins.
    /// </summary>
    [Fact]
    public void AHigherPriorityGroupWins()
    {
        var groups = new[]
        {
            new SettingsGroup { Id = Guid.NewGuid(), Name = "b", Priority = 10 },
            new SettingsGroup { Id = Guid.NewGuid(), Name = "a", Priority = 1 }
        };

        var ordered = SettingsResolver.InLayerOrder(groups);

        Assert.Equal("a", ordered[0].Name);
        Assert.Equal("b", ordered[1].Name);
    }

    /// <summary>
    /// Groups sharing a priority are ordered by id. Arbitrary, but the same caller in
    /// the same groups has to resolve to the same answer every time rather than to
    /// whatever the database returned first.
    /// </summary>
    [Fact]
    public void GroupsOnTheSamePriorityAreOrderedStably()
    {
        var first = new SettingsGroup { Id = new Guid("00000000-0000-0000-0000-000000000001"), Name = "one" };
        var second = new SettingsGroup { Id = new Guid("00000000-0000-0000-0000-000000000002"), Name = "two" };

        var oneWay = SettingsResolver.InLayerOrder(new[] { second, first }).Select(g => g.Name);
        var other = SettingsResolver.InLayerOrder(new[] { first, second }).Select(g => g.Name);

        Assert.Equal(oneWay, other);
        Assert.Equal("one", oneWay.First());
    }

    /// <summary>
    /// Redacting drops the credentials and leaves everything else alone. The key is
    /// absent rather than blanked, so a client cannot tell an administrator who
    /// cleared the field from a user who may not see it, and cannot push an empty
    /// string back as though it were the real value.
    /// </summary>
    [Fact]
    public void RedactingRemovesOnlyTheSecrets()
    {
        var settings = new Settings
        {
            jellyseerrApiKey = new Lockable<string> { locked = true, value = "a-real-key" },
            jellyseerrServerUrl = new Lockable<string> { locked = true, value = "https://seerr.example" },
            subtitleSize = new Lockable<int> { locked = false, value = 80 }
        };

        var redacted = SettingsResolver.Redact(settings);

        Assert.Null(redacted.jellyseerrApiKey);
        Assert.Equal("https://seerr.example", redacted.jellyseerrServerUrl?.value);
        Assert.Equal(80, redacted.subtitleSize?.value);
        Assert.Equal("a-real-key", settings.jellyseerrApiKey?.value);
    }
}
