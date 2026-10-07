using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    /// Two changes at once both land: two buttons tapped in a row, or two devices of one
    /// account, each read the choices before the other stored them and one change was lost.
    /// </summary>
    [Fact]
    public async Task TwoChangesAtOnceBothLand()
    {
        var user = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        using var firstHasRead = new ManualResetEventSlim();
        using var secondHasRead = new ManualResetEventSlim();

        var one = Task.Run(() => _db.ChangeNotificationPreferences(user, mine =>
        {
            firstHasRead.Set();
            // Without the write lock the second change reads now, before this one is stored.
            secondHasRead.Wait(TimeSpan.FromMilliseconds(500));
            mine.MutedShows.Add(first);
            return null;
        }));

        firstHasRead.Wait();
        var other = Task.Run(() => _db.ChangeNotificationPreferences(user, mine =>
        {
            secondHasRead.Set();
            mine.MutedShows.Add(second);
            return null;
        }));

        await Task.WhenAll(one, other);
        Assert.Equal(new[] { first, second }.Order(), _db.GetNotificationPreferences(user)!.MutedShows.Order());
    }

    /// <summary>
    /// A save waits for a change in progress and then replaces it, rather than failing on the
    /// row the change was about to write for somebody who had none.
    /// </summary>
    [Fact]
    public async Task ASaveWaitsForAChangeInProgress()
    {
        var user = Guid.NewGuid();
        using var changing = new ManualResetEventSlim();

        var change = Task.Run(() => _db.ChangeNotificationPreferences(user, mine =>
        {
            changing.Set();
            Thread.Sleep(300);
            mine.MutedShows.Add(Guid.NewGuid());
            return null;
        }));

        changing.Wait();
        _db.SaveNotificationPreferences(user, new NotificationPreferences { Pause = new NotificationPause() });
        await change;

        var stored = _db.GetNotificationPreferences(user)!;
        Assert.True(stored.IsPaused(DateTime.UtcNow));
        Assert.Empty(stored.MutedShows);
    }

    /// <summary>
    /// A change that is refused stores nothing.
    /// </summary>
    [Fact]
    public void ARefusedChangeStoresNothing()
    {
        var user = Guid.NewGuid();

        var (_, problem) = _db.ChangeNotificationPreferences(user, mine =>
        {
            mine.MutedShows.Add(Guid.NewGuid());
            return "No.";
        });

        Assert.Equal("No.", problem);
        Assert.Null(_db.GetNotificationPreferences(user));
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
