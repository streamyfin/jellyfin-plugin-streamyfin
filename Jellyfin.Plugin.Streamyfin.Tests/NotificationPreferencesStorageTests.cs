using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.Db;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A person's choices, stored apart from what an administrator says about them.
/// </summary>
public class NotificationPreferencesStorageTests : IDisposable
{
    private readonly string _directory = TestDirectory.Create();
    private readonly PluginDatabase _db;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationPreferencesStorageTests"/>
    /// class.
    /// </summary>
    public NotificationPreferencesStorageTests()
    {
        _db = new PluginDatabase(_directory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try { System.IO.Directory.Delete(_directory, recursive: true); } catch (System.IO.IOException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Somebody who never chose anything has nothing stored.
    /// </summary>
    [Fact]
    public void SomebodyWhoNeverChoseHasNothingStored()
    {
        Assert.Null(_db.GetNotificationPreferences(Guid.NewGuid()));
    }

    /// <summary>
    /// Choices are read back as they were saved.
    /// </summary>
    [Fact]
    public void ChoicesAreReadBackAsSaved()
    {
        var user = Guid.NewGuid();
        var mine = new NotificationPreferences { MutedLibraries = [Guid.NewGuid()] };
        mine.Events["itemAdded"] = false;

        _db.SaveNotificationPreferences(user, mine);
        var read = _db.GetNotificationPreferences(user);

        Assert.NotNull(read);
        Assert.False(read!.Keeps("itemAdded"));
        Assert.Equal(mine.MutedLibraries, read.MutedLibraries);
    }

    /// <summary>
    /// Saving again replaces what was saved before.
    /// </summary>
    [Fact]
    public void SavingAgainReplaces()
    {
        var user = Guid.NewGuid();
        _db.SaveNotificationPreferences(user, new NotificationPreferences { Pause = new NotificationPause() });
        _db.SaveNotificationPreferences(user, new NotificationPreferences());

        Assert.False(_db.GetNotificationPreferences(user)!.IsPaused(DateTime.UtcNow));
    }

    /// <summary>
    /// An administrator clearing a user's level must not take the person's own choices with it.
    /// </summary>
    [Fact]
    public void ClearingTheUserLevelLeavesTheirChoices()
    {
        var user = Guid.NewGuid();
        _db.SaveUserSettingsOverride(user, "{}", "{}");
        _db.SaveNotificationPreferences(user, new NotificationPreferences { Pause = new NotificationPause() });

        _db.RemoveUserSettingsOverride(user);

        Assert.NotNull(_db.GetNotificationPreferences(user));
    }

    /// <summary>
    /// Everyone's choices are read in one go.
    /// </summary>
    [Fact]
    public void EveryoneIsReadAtOnce()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        _db.SaveNotificationPreferences(alice, new NotificationPreferences());
        _db.SaveNotificationPreferences(bob, new NotificationPreferences { Pause = new NotificationPause() });

        var all = _db.AllNotificationPreferences();

        Assert.Equal(2, all.Count);
        Assert.True(all[bob].IsPaused(DateTime.UtcNow));
    }

    /// <summary>
    /// A restore replaces everyone's choices, removing those the file does not carry.
    /// </summary>
    [Fact]
    public void ARestoreReplacesEveryone()
    {
        var before = Guid.NewGuid();
        var after = Guid.NewGuid();
        _db.SaveNotificationPreferences(before, new NotificationPreferences());

        _db.ReplaceNotificationPreferences([(after, new NotificationPreferences())]);

        Assert.Null(_db.GetNotificationPreferences(before));
        Assert.NotNull(_db.GetNotificationPreferences(after));
    }
}
