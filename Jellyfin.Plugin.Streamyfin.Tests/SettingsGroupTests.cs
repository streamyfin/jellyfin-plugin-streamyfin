using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Jellyfin.Plugin.Streamyfin.Db;
using Microsoft.EntityFrameworkCore;
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

        _db.SaveUserSettingsOverride(
            userId,
            _serialization.SerializeToJson(new Settings
            {
                forwardSkipTime = new Lockable<int> { locked = true, value = 5 }
            }),
            "{}");

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
    /// A level that turns the playback quality off keeps every other setting it carries.
    /// </summary>
    /// <remarks>
    /// The quality is the one setting whose "no cap" is a null, and the store writes
    /// with Jellyfin's JSON options, which omit a null: the key left the document
    /// entirely. Reading it back then failed on <c>Lockable&lt;T&gt;.value</c> being
    /// required, the tolerant read answered null, and the whole level came back empty.
    /// A group set to Max looked saved until the page was reopened, and the resolution
    /// gave its members nothing at all. Seen on the beta on 2026-09-11.
    /// </remarks>
    [Fact]
    public void ALevelThatSetsTheQualityToMaxKeepsItsOtherSettings()
    {
        var json = _serialization.SerializeToJson(new Settings
        {
            forwardSkipTime = new Lockable<int> { value = 42, locked = true },
            defaultBitrate = new Lockable<Bitrate?> { value = null, locked = false },
        });

        var read = _serialization.DeserializeJson<Settings>(json);

        Assert.NotNull(read);
        Assert.Equal(42, read!.forwardSkipTime!.value);
        Assert.NotNull(read.defaultBitrate);
        Assert.Null(read.defaultBitrate!.value);
        Assert.False(read.defaultBitrate.locked);
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
    /// The settings stored as numbers survive being stored and read back. The plugin
    /// stores <c>OrientationLock</c>, <c>Bitrate</c> and <c>SubtitlePlaybackMode</c> as
    /// numbers, and a stored level comes back through the JSON options that wrote it.
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
        _db.SaveUserSettingsOverride(userId, "{}", "{}");

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

    /// <summary>
    /// The warning about an unreadable level stays on one line, whatever it is told
    /// the level is called.
    /// </summary>
    /// <remarks>
    /// Callers name a level by its id, so this is defence rather than a live hole,
    /// but a log line assembled from a name someone typed is how a forged entry gets
    /// written, and this one only appears when stored data is already wrong.
    /// </remarks>
    [Fact]
    public void TheWarningAboutAnUnreadableLevelStaysOnOneLine()
    {
        var log = new RecordingLogger<SettingsResolutionService>();
        var resolution = new SettingsResolutionService(_serialization, log);

        Assert.Null(resolution.ReadLevel("{ this is not json", "group Night\r\nfatal: everything is fine"));

        var written = Assert.Single(log.Messages);
        Assert.DoesNotContain("\n", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", written, StringComparison.Ordinal);
        Assert.Contains("group Night", written, StringComparison.Ordinal);
        Assert.Contains("fatal: everything is fine", written, StringComparison.Ordinal);
    }

    /// <summary>
    /// The line and paragraph separators end a line in some readers without being control
    /// characters, so they are cleaned as well.
    /// </summary>
    [Fact]
    public void TheWarningAboutAnUnreadableLevelKeepsTheUnicodeSeparatorsOut()
    {
        var log = new RecordingLogger<SettingsResolutionService>();
        var resolution = new SettingsResolutionService(_serialization, log);

        Assert.Null(resolution.ReadLevel("{ this is not json", "group Night\u2028fatal\u2029everything is fine"));

        var written = Assert.Single(log.Messages);
        Assert.DoesNotContain("\u2028", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2029", written, StringComparison.Ordinal);
        Assert.Contains("group Night fatal everything is fine", written, StringComparison.Ordinal);
    }

    /// <summary>
    /// A level named by nothing at all still says something.
    /// </summary>
    [Fact]
    public void ALevelNamedByNothingStillSaysSomething()
    {
        var log = new RecordingLogger<SettingsResolutionService>();
        var resolution = new SettingsResolutionService(_serialization, log);

        Assert.Null(resolution.ReadLevel("{ this is not json", "   "));

        Assert.Contains("an unnamed level", Assert.Single(log.Messages), StringComparison.Ordinal);
    }

    private static SettingsGroup Named(string name, Guid? id = null) =>
        new() { Id = id ?? Guid.Empty, Name = name, SettingsJson = "{}" };

    /// <summary>
    /// A group saved under a name another group has is refused by the database, as a type the
    /// routes answer with a 400. The unique index refused it before as a 500.
    /// </summary>
    [Fact]
    public void ANameInUseIsRefused()
    {
        _db.SaveSettingsGroup(Named("Kids"));

        var taken = Assert.Throws<GroupNameTakenException>(() => _db.SaveSettingsGroup(Named("Kids")));

        Assert.Equal("Kids", taken.Name);
        Assert.Equal("A group named \"Kids\" exists already.", taken.Message);
        Assert.Single(_db.GetSettingsGroups());
    }

    /// <summary>
    /// A rename to another group's name is refused, and the group keeps its own name freely.
    /// </summary>
    [Fact]
    public void ARenameCannotTakeAnotherGroupsName()
    {
        var kids = _db.SaveSettingsGroup(Named("Kids"));
        var adults = _db.SaveSettingsGroup(Named("Adults"));

        Assert.Throws<GroupNameTakenException>(() => _db.SaveSettingsGroup(Named("Kids", adults.Id)));
        _db.SaveSettingsGroup(Named("Kids", kids.Id));

        Assert.Equal(new[] { "Adults", "Kids" }, _db.GetSettingsGroups().Select(g => g.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Compared the way the database compares them, where "Kids" and "kids" are two names.
    /// </summary>
    [Fact]
    public void ANameDifferingInCaseIsAnotherName()
    {
        _db.SaveSettingsGroup(Named("Kids"));
        _db.SaveSettingsGroup(Named("kids"));

        Assert.Equal(2, _db.GetSettingsGroups().Count);
    }

    /// <summary>
    /// Two saves of one name at the same moment leave one group, and the other is the same
    /// refusal: a check made before the write would have let both through to a 500.
    /// </summary>
    [Fact]
    public void TwoSavesOfOneNameAtOnceLeaveOneGroup()
    {
        for (var round = 0; round < 20; round++)
        {
            var name = $"Twins {round}";
            var refused = 0;

            System.Threading.Tasks.Parallel.Invoke(
                () => { try { _db.SaveSettingsGroup(Named(name)); } catch (GroupNameTakenException) { System.Threading.Interlocked.Increment(ref refused); } },
                () => { try { _db.SaveSettingsGroup(Named(name)); } catch (GroupNameTakenException) { System.Threading.Interlocked.Increment(ref refused); } });

            Assert.Equal(1, refused);
            Assert.Single(_db.GetSettingsGroups(), group => group.Name == name);
        }
    }

    /// <summary>
    /// An update of a group another administrator deleted meanwhile does not bring it back.
    /// The save added a group whose id it could not find, with the name and settings of the
    /// update and none of its members, and the route answered 200 (#248).
    /// </summary>
    [Fact]
    public void AnUpdateOfADeletedGroupDoesNotBringItBack()
    {
        var kids = _db.SaveSettingsGroup(Named("Kids"));
        _db.RemoveSettingsGroup(kids.Id);

        var gone = Assert.Throws<GroupGoneException>(() => _db.SaveSettingsGroup(Named("Children", kids.Id)));

        Assert.Equal(kids.Id, gone.Id);
        Assert.Empty(_db.GetSettingsGroups());
    }

    /// <summary>
    /// An update writes the row even when it changes nothing, so a group deleted between the
    /// read and the write is found gone. EF sends no UPDATE for values that did not change,
    /// and the route then answered 200 for a group that was no longer there.
    /// </summary>
    [Fact]
    public void AnUpdateThatChangesNothingStillWritesTheRow()
    {
        var kids = _db.SaveSettingsGroup(Named("Kids"));

        using (var context = _db.CreateContext())
        {
            context.Database.ExecuteSqlRaw(
                "CREATE TABLE Writes (Value INTEGER NOT NULL); INSERT INTO Writes VALUES (0); "
                + "CREATE TRIGGER CountWrites AFTER UPDATE ON SettingsGroups BEGIN UPDATE Writes SET Value = Value + 1; END;");
        }

        _db.SaveSettingsGroup(Named("Kids", kids.Id));

        using var read = _db.CreateContext();
        Assert.Equal(1, read.Database.SqlQueryRaw<int>("SELECT Value FROM Writes").AsEnumerable().Single());
    }

    /// <summary>
    /// The store holds a name to the routes' rule, so a writer other than the routes cannot
    /// store one they refuse, and stores it tidied.
    /// </summary>
    [Fact]
    public void TheStoreRefusesANameTheRoutesRefuse()
    {
        Assert.Throws<ArgumentException>(() => _db.SaveSettingsGroup(Named("Kids\nClub")));
        Assert.Throws<ArgumentException>(() => _db.ReplaceTargeting([(Named("Kids\u202Eclub"), Array.Empty<Guid>())], []));

        _db.SaveSettingsGroup(Named("  Kids "));

        Assert.Equal("Kids", Assert.Single(_db.GetSettingsGroups()).Name);
    }

    /// <summary>
    /// Two deletes of one group at the same moment both succeed. The second read the row, found
    /// it gone on writing, and EF's concurrency exception reached the route as a 500.
    /// </summary>
    [Fact]
    public void TwoDeletesOfOneGroupAtOnceBothSucceed()
    {
        for (var round = 0; round < 40; round++)
        {
            var group = _db.SaveSettingsGroup(Named($"Twice {round}"));

            System.Threading.Tasks.Parallel.Invoke(() => _db.RemoveSettingsGroup(group.Id), () => _db.RemoveSettingsGroup(group.Id));

            Assert.Null(_db.GetSettingsGroup(group.Id));
        }
    }

    /// <summary>
    /// Members set for a group deleted meanwhile are not stored: the rows went in without a
    /// group, and the route answered 200 with the members of a group that was gone.
    /// </summary>
    [Fact]
    public void MembersOfADeletedGroupAreNotStored()
    {
        var group = _db.SaveSettingsGroup(Named("Gone"));
        _db.RemoveSettingsGroup(group.Id);

        var gone = Assert.Throws<GroupGoneException>(() => _db.SetGroupMembers(group.Id, [Guid.NewGuid()]));

        Assert.Equal(group.Id, gone.Id);
        Assert.Empty(MemberRowsOf(group.Id));
    }

    /// <summary>
    /// Members set while the group is deleted leave no row behind, whichever lands first.
    /// </summary>
    [Fact]
    public void MembersRacingADeleteLeaveNoRowBehind()
    {
        for (var round = 0; round < 40; round++)
        {
            var group = _db.SaveSettingsGroup(Named($"Members {round}"));

            System.Threading.Tasks.Parallel.Invoke(
                () => { try { _db.SetGroupMembers(group.Id, [Guid.NewGuid(), Guid.NewGuid()]); } catch (GroupGoneException) { } },
                () => _db.RemoveSettingsGroup(group.Id));

            Assert.Empty(MemberRowsOf(group.Id));
        }
    }

    private List<Guid> MemberRowsOf(Guid groupId)
    {
        using var context = _db.CreateContext();
        return context.SettingsGroupMembers.AsNoTracking().Where(m => m.GroupId == groupId).Select(m => m.UserId).ToList();
    }

    /// <summary>
    /// A name stored before the routes checked them is repaired at the next start, so the
    /// server's own backup restores: the restore holds a file to the routes' rule, and refused
    /// one carrying such a name, or two names a space apart. A name that was fine stays, and the
    /// log names each change.
    /// </summary>
    [Fact]
    public void StoredNamesTheRoutesRefuseAreRepairedAtTheNextStart()
    {
        var fine = Guid.NewGuid();
        using (var context = _db.CreateContext())
        {
            context.SettingsGroups.AddRange(
                new SettingsGroup { Id = fine, Name = "Adults", SettingsJson = "{}" },
                new SettingsGroup { Id = Guid.NewGuid(), Name = "Kids ", SettingsJson = "{}" },
                new SettingsGroup { Id = Guid.NewGuid(), Name = "Kids\t", SettingsJson = "{}" },
                new SettingsGroup { Id = Guid.NewGuid(), Name = "Night\nshift", SettingsJson = "{}" },
                new SettingsGroup { Id = Guid.NewGuid(), Name = new string('a', 150), SettingsJson = "{}" },
                new SettingsGroup { Id = Guid.NewGuid(), Name = "\u202E", SettingsJson = "{}" });
            context.SaveChanges();
        }

        var logger = new RecordingLogger<PluginDatabase>();
        var restarted = new PluginDatabase(_directory, logger);

        var names = restarted.GetSettingsGroups().Select(g => g.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "Adults", "Group", "Kids", "Kids (2)", "Night shift", new string('a', 100) }, names);
        Assert.Equal("Adults", restarted.GetSettingsGroup(fine)!.Name);
        Assert.Contains(logger.Messages, message => message.Contains("Renamed 5 group(s)", StringComparison.Ordinal));

        var backup = new Api.ConfigurationBackup
        {
            Groups = [.. restarted.GetSettingsGroups().Select(g => new Api.SettingsGroupDto { Id = g.Id, Name = g.Name })]
        };
        Assert.Null(backup.TargetingProblem());
    }

    /// <summary>
    /// An update and a delete of one group at the same moment leave no group, whichever lands
    /// first, including a delete that lands between the update reading the group and writing it.
    /// </summary>
    [Fact]
    public void AnUpdateRacingADeleteNeverLeavesTheGroup()
    {
        for (var round = 0; round < 40; round++)
        {
            var group = _db.SaveSettingsGroup(Named($"Racing {round}"));

            System.Threading.Tasks.Parallel.Invoke(
                () => { try { _db.SaveSettingsGroup(Named($"Renamed {round}", group.Id)); } catch (GroupGoneException) { } },
                () => _db.RemoveSettingsGroup(group.Id));

            Assert.Null(_db.GetSettingsGroup(group.Id));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        TestDirectory.Delete(_directory);
        GC.SuppressFinalize(this);
    }
}
