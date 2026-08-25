using System;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Jellyfin.Plugin.Streamyfin.Db;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Storing the targeting levels, and reading them back through the resolution
/// service, which is where the stored JSON meets the rule.
/// </summary>
public class SettingsGroupTests : IDisposable
{
    private readonly string _directory;
    private readonly PluginDatabase _db;
    private readonly SerializationHelper _serialization = new();
    private readonly SettingsResolutionService _resolution;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsGroupTests"/> class.
    /// </summary>
    public SettingsGroupTests()
    {
        _directory = TestDirectory.Create();
        _db = new PluginDatabase(_directory);
        _resolution = new SettingsResolutionService(_serialization);
    }

    /// <summary>
    /// A group round trips, including the settings it carries.
    /// </summary>
    [Fact]
    public void AGroupRoundTrips()
    {
        var saved = _db.SaveSettingsGroup(new SettingsGroup
        {
            Name = "Kids",
            Priority = 5,
            SettingsJson = _serialization.SerializeToJson(new Settings
            {
                subtitleSize = new Lockable<int> { locked = true, value = 120 }
            })
        });

        Assert.NotEqual(Guid.Empty, saved.Id);

        var read = _db.GetSettingsGroup(saved.Id);

        Assert.NotNull(read);
        Assert.Equal("Kids", read!.Name);
        Assert.Equal(5, read.Priority);

        var settings = _serialization.DeserializeJson<Settings>(read.SettingsJson);
        Assert.Equal(120, settings?.subtitleSize?.value);
    }

    /// <summary>
    /// Saving with an id updates in place rather than adding a second group.
    /// </summary>
    [Fact]
    public void SavingAnExistingGroupUpdatesIt()
    {
        var saved = _db.SaveSettingsGroup(new SettingsGroup { Name = "Kids", Priority = 1 });

        _db.SaveSettingsGroup(new SettingsGroup { Id = saved.Id, Name = "Children", Priority = 2 });

        Assert.Single(_db.GetSettingsGroups());
        Assert.Equal("Children", _db.GetSettingsGroup(saved.Id)?.Name);
    }

    /// <summary>
    /// Deleting a group takes its memberships with it, in one transaction. Rows left
    /// behind would resolve to nothing and would come back if the id were reused.
    /// </summary>
    [Fact]
    public void DeletingAGroupTakesItsMembershipsWithIt()
    {
        var userId = Guid.NewGuid();
        var group = _db.SaveSettingsGroup(new SettingsGroup { Name = "Kids" });
        _db.SetGroupMembers(group.Id, [userId]);

        _db.RemoveSettingsGroup(group.Id);

        Assert.Empty(_db.GetSettingsGroups());
        Assert.Empty(_db.GetGroupsForUser(userId));
        Assert.Empty(_db.GetGroupMembers(group.Id));
    }

    /// <summary>
    /// Setting the members replaces them, and the same user added twice is one row.
    /// </summary>
    [Fact]
    public void SettingMembersReplacesThemAndDeduplicates()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var group = _db.SaveSettingsGroup(new SettingsGroup { Name = "Kids" });

        _db.SetGroupMembers(group.Id, [alice, alice, bob]);
        Assert.Equal(2, _db.GetGroupMembers(group.Id).Count);

        _db.SetGroupMembers(group.Id, [bob]);
        Assert.Equal([bob], _db.GetGroupMembers(group.Id));
    }

    /// <summary>
    /// A user's groups come back in layer order, lowest priority first.
    /// </summary>
    [Fact]
    public void AUsersGroupsComeBackInLayerOrder()
    {
        var userId = Guid.NewGuid();
        var low = _db.SaveSettingsGroup(new SettingsGroup { Name = "Everyone", Priority = 1 });
        var high = _db.SaveSettingsGroup(new SettingsGroup { Name = "Staff", Priority = 10 });

        _db.SetGroupMembers(low.Id, [userId]);
        _db.SetGroupMembers(high.Id, [userId]);

        var groups = _db.GetGroupsForUser(userId);

        Assert.Equal(["Everyone", "Staff"], groups.Select(g => g.Name));
    }

    /// <summary>
    /// The three levels resolve through the service the way they resolve in the rule.
    /// </summary>
    [Fact]
    public void TheThreeLevelsResolveEndToEnd()
    {
        var userId = Guid.NewGuid();

        var group = _db.SaveSettingsGroup(new SettingsGroup
        {
            Name = "Staff",
            Priority = 1,
            SettingsJson = _serialization.SerializeToJson(new Settings
            {
                subtitleSize = new Lockable<int> { locked = false, value = 60 }
            })
        });
        _db.SetGroupMembers(group.Id, [userId]);

        _db.SaveUserSettingsOverride(userId, _serialization.SerializeToJson(new Settings
        {
            forwardSkipTime = new Lockable<int> { locked = true, value = 5 }
        }));

        var global = new Settings
        {
            subtitleSize = new Lockable<int> { locked = true, value = 80 },
            forwardSkipTime = new Lockable<int> { locked = false, value = 30 },
            rewindSkipTime = new Lockable<int> { locked = false, value = 10 }
        };

        var resolved = _resolution.Resolve(
            global,
            _db.GetGroupsForUser(userId),
            _db.GetUserSettingsOverride(userId));

        // The group unlocked and shrank the subtitles.
        Assert.Equal(60, resolved.subtitleSize?.value);
        Assert.False(resolved.subtitleSize?.locked);

        // The user level locked the skip time.
        Assert.Equal(5, resolved.forwardSkipTime?.value);
        Assert.True(resolved.forwardSkipTime?.locked);

        // Nobody spoke about the rewind, so the server's value stands.
        Assert.Equal(10, resolved.rewindSkipTime?.value);
    }

    /// <summary>
    /// A level whose JSON cannot be read costs that level, not every setting for
    /// every user in it. This runs during a request, so throwing would turn one
    /// corrupted row into a broken settings endpoint.
    /// </summary>
    [Fact]
    public void AnUnreadableLevelIsSkippedRatherThanThrown()
    {
        var userId = Guid.NewGuid();
        var group = _db.SaveSettingsGroup(new SettingsGroup
        {
            Name = "Broken",
            SettingsJson = "{ this is not json"
        });
        _db.SetGroupMembers(group.Id, [userId]);

        var resolved = _resolution.Resolve(
            new Settings { subtitleSize = new Lockable<int> { locked = true, value = 80 } },
            _db.GetGroupsForUser(userId),
            null);

        Assert.Equal(80, resolved.subtitleSize?.value);
    }

    /// <summary>
    /// The settings served as numbers survive being stored and read back. The plugin
    /// writes <c>OrientationLock</c>, <c>Bitrate</c> and <c>SubtitlePlaybackMode</c> as
    /// numbers, and the YAML reader the rest of the plugin uses expects the member
    /// name, so a level stored as JSON has to come back through the JSON reader.
    /// </summary>
    [Fact]
    public void TheNumericEnumSettingsSurviveTheRoundTrip()
    {
        var stored = _serialization.SerializeToJson(new Settings
        {
            defaultVideoOrientation = new Lockable<OrientationLock> { locked = true, value = OrientationLock.Landscape },
            subtitleMode = new Lockable<SubtitlePlaybackMode> { locked = false, value = SubtitlePlaybackMode.Smart }
        });

        var read = _serialization.DeserializeJson<Settings>(stored);

        Assert.Equal(OrientationLock.Landscape, read?.defaultVideoOrientation?.value);
        Assert.Equal(SubtitlePlaybackMode.Smart, read?.subtitleMode?.value);
    }

    /// <summary>
    /// Clearing a user's override leaves them on their groups. The two levels are
    /// stored apart and have to stay apart: taking a targeted setting back from
    /// someone should not quietly drop them out of every group they are in.
    /// </summary>
    [Fact]
    public void ClearingAUserOverrideLeavesTheirGroups()
    {
        var userId = Guid.NewGuid();
        var group = _db.SaveSettingsGroup(new SettingsGroup { Name = "Staff" });
        _db.SetGroupMembers(group.Id, [userId]);
        _db.SaveUserSettingsOverride(userId, "{}");

        _db.RemoveUserSettingsOverride(userId);

        Assert.Null(_db.GetUserSettingsOverride(userId));
        Assert.Equal(["Staff"], _db.GetGroupsForUser(userId).Select(g => g.Name));
    }

    /// <summary>
    /// A stored level that cannot be read comes back as nothing rather than throwing,
    /// on every path that reads one.
    /// </summary>
    /// <remarks>
    /// Nothing validates the JSON on the way in, so a row edited outside the plugin
    /// or a partial write can leave one that does not parse. Resolution skipping it
    /// is not enough on its own: the administration API reads the same rows to list
    /// the groups, and throwing there would make <c>GET groups</c> fail as a whole,
    /// so an administrator could no longer see the group in order to repair it.
    /// </remarks>
    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnreadableLevelReadsAsNothing(string stored)
    {
        Assert.Null(_resolution.ReadLevel(stored, "a test"));
    }

    /// <summary>
    /// A readable level still comes back through the same call.
    /// </summary>
    [Fact]
    public void AReadableLevelComesBack()
    {
        var stored = _serialization.SerializeToJson(new Settings
        {
            subtitleSize = new Lockable<int> { locked = true, value = 42 }
        });

        Assert.Equal(42, _resolution.ReadLevel(stored, "a test")?.subtitleSize?.value);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        TestDirectory.Delete(_directory);
        GC.SuppressFinalize(this);
    }
}
