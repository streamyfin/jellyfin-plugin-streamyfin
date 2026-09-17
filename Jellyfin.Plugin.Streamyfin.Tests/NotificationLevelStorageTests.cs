using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Streamyfin.Db;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Storing what a group or a user says about the notifications they get.
/// </summary>
/// <remarks>
/// The levels are the same ones the settings use, and the two are kept in separate
/// columns: the settings are served to the app, the notifications are the server's
/// business, and one must not leak into the other.
/// </remarks>
public class NotificationLevelStorageTests : IDisposable
{
    private readonly string _directory;
    private readonly PluginDatabase _db;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationLevelStorageTests"/> class.
    /// </summary>
    public NotificationLevelStorageTests()
    {
        _directory = TestDirectory.Create();
        _db = new PluginDatabase(_directory);
    }

    private const string TaskFailedOn = """{"taskFailed":{"enabled":true}}""";
    private const string TaskFailedOff = """{"taskFailed":{"enabled":false}}""";

    /// <summary>
    /// What a group says about an event survives being saved and read back, and changing
    /// it changes it.
    /// </summary>
    [Fact]
    public void AGroupKeepsWhatItSaysAboutAnEvent()
    {
        var saved = _db.SaveSettingsGroup(new SettingsGroup
        {
            Name = "Night shift",
            Priority = 1,
            SettingsJson = "{}",
            NotificationsJson = TaskFailedOn
        });

        Assert.Equal(TaskFailedOn, _db.GetSettingsGroup(saved.Id)!.NotificationsJson);

        _db.SaveSettingsGroup(new SettingsGroup
        {
            Id = saved.Id,
            Name = "Night shift",
            Priority = 1,
            SettingsJson = "{}",
            NotificationsJson = TaskFailedOff
        });

        Assert.Equal(TaskFailedOff, _db.GetSettingsGroup(saved.Id)!.NotificationsJson);
    }

    /// <summary>
    /// The same for one user.
    /// </summary>
    [Fact]
    public void AUserKeepsWhatTheySayAboutAnEvent()
    {
        var userId = Guid.NewGuid();

        _db.SaveUserSettingsOverride(userId, "{}", TaskFailedOn);

        Assert.Equal(TaskFailedOn, _db.GetUserSettingsOverride(userId)!.NotificationsJson);

        _db.SaveUserSettingsOverride(userId, "{}", TaskFailedOff);

        Assert.Equal(TaskFailedOff, _db.GetUserSettingsOverride(userId)!.NotificationsJson);
    }

    /// <summary>
    /// What the levels say, gathered per user the way a send asks for it: the groups they
    /// are in first, in priority order, then their own.
    /// </summary>
    [Fact]
    public void TheLevelsAreGatheredPerUserInOrder()
    {
        var userId = Guid.NewGuid();

        var lower = _db.SaveSettingsGroup(new SettingsGroup
        {
            Name = "Everybody on call",
            Priority = 1,
            SettingsJson = "{}",
            NotificationsJson = TaskFailedOn
        });

        var higher = _db.SaveSettingsGroup(new SettingsGroup
        {
            Name = "On holiday",
            Priority = 5,
            SettingsJson = "{}",
            NotificationsJson = TaskFailedOff
        });

        _db.SetGroupMembers(lower.Id, [userId]);
        _db.SetGroupMembers(higher.Id, [userId]);
        _db.SaveUserSettingsOverride(userId, "{}", TaskFailedOn);

        Assert.Equal([TaskFailedOn, TaskFailedOff, TaskFailedOn], _db.NotificationLevels()[userId]);
    }

    /// <summary>
    /// Saving settings for somebody does not throw away what they said about their
    /// notifications, and the other way round.
    /// </summary>
    [Fact]
    public void TheTwoSidesDoNotOverwriteEachOther()
    {
        var userId = Guid.NewGuid();

        _db.SaveUserSettingsOverride(userId, """{"forwardSkipTime":{"value":30}}""", TaskFailedOn);

        var stored = _db.GetUserSettingsOverride(userId)!;

        Assert.Equal("""{"forwardSkipTime":{"value":30}}""", stored.SettingsJson);
        Assert.Equal(TaskFailedOn, stored.NotificationsJson);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        TestDirectory.Delete(_directory);
        GC.SuppressFinalize(this);
    }
}
