using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Db;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Behaviour of the plugin database over EF Core.
/// </summary>
/// <remarks>
/// Each test gets its own directory, since xunit builds a fresh instance per test.
/// That is what removed the purge before and after every test the hand written
/// store needed, along with the ordering it implied.
/// </remarks>
public class DatabaseTests : IDisposable
{
    private readonly string _directory;
    private readonly PluginDatabase _db;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseTests"/> class.
    /// </summary>
    public DatabaseTests()
    {
        _directory = TestDirectory.Create();
        _db = new PluginDatabase(_directory);
    }

    /// <summary>
    /// Registering a device that already has a token replaces it rather than
    /// leaving two rows for the same device.
    /// </summary>
    [Fact]
    public void RegisteringTheSameDeviceReplacesItsToken()
    {
        var deviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _db.AddDeviceToken(new DeviceToken { DeviceId = deviceId, Token = "first", UserId = userId });
        var second = _db.AddDeviceToken(new DeviceToken { DeviceId = deviceId, Token = "second", UserId = userId });

        var stored = _db.GetDeviceTokenForDeviceId(deviceId);

        Assert.NotNull(stored);
        Assert.Equal("second", stored.Token);
        Assert.Equal(userId, stored.UserId);
        Assert.Equal(second.Timestamp, stored.Timestamp);
        Assert.Equal(1, _db.TotalDevicesCount());
    }

    /// <summary>
    /// Two registrations of the same device at the same time both succeed and leave
    /// one row.
    /// </summary>
    /// <remarks>
    /// The app posts its push token twice on sign in. With a lookup followed by an
    /// insert, the second post found nothing, inserted, and failed on the primary key
    /// with a 500 while the first was still saving. Seen on the beta on 2026-09-11.
    /// The write is one statement now, so there is no window between the two.
    /// </remarks>
    [Fact]
    public void TwoRegistrationsOfTheSameDeviceAtOnceBothSucceed()
    {
        var deviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        for (var round = 0; round < 30; round++)
        {
            // Each round starts with no row, or only the first one could ever race: a
            // lookup followed by an insert takes the update path once the row exists.
            _db.RemoveDeviceToken(deviceId);
            System.Threading.Tasks.Parallel.Invoke(
                () => _db.AddDeviceToken(new DeviceToken { DeviceId = deviceId, Token = "left", UserId = userId }),
                () => _db.AddDeviceToken(new DeviceToken { DeviceId = deviceId, Token = "right", UserId = userId }));
        }

        var stored = _db.GetDeviceTokenForDeviceId(deviceId);

        Assert.NotNull(stored);
        Assert.Contains(stored.Token, new[] { "left", "right" });
        Assert.Equal(1, _db.TotalDevicesCount());
    }

    /// <summary>
    /// Two removals of the same device at once both succeed. The app signs out through
    /// an effect that can run twice, as its registration did.
    /// </summary>
    [Fact]
    public void TwoRemovalsOfTheSameDeviceAtOnceBothSucceed()
    {
        var deviceId = Guid.NewGuid();

        for (var round = 0; round < 30; round++)
        {
            _db.AddDeviceToken(new DeviceToken { DeviceId = deviceId, Token = "token", UserId = Guid.NewGuid() });

            System.Threading.Tasks.Parallel.Invoke(
                () => _db.RemoveDeviceToken(deviceId),
                () => _db.RemoveDeviceToken(deviceId));

            Assert.Null(_db.GetDeviceTokenForDeviceId(deviceId));
        }
    }

    /// <summary>
    /// A token registered by a second device leaves the first one behind.
    /// </summary>
    /// <remarks>
    /// Expo keeps the token of an iOS installation through an uninstall and a reinstall,
    /// while the app starts over with a new device id. The account signed in before the
    /// reinstall kept its row, and Expo never reports that token as gone, so that
    /// account's notifications reached whoever signed in after it.
    /// </remarks>
    [Fact]
    public void ATokenBelongsToTheDeviceThatRegisteredItLast()
    {
        var before = Guid.NewGuid();
        var after = Guid.NewGuid();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        _db.AddDeviceToken(new DeviceToken { DeviceId = before, Token = "reinstalled", UserId = alice });
        _db.AddDeviceToken(new DeviceToken { DeviceId = after, Token = "reinstalled", UserId = bob });

        var stored = Assert.Single(_db.GetAllDeviceTokens());
        Assert.Equal(after, stored.DeviceId);
        Assert.Equal(bob, stored.UserId);
    }

    /// <summary>
    /// The account signed in before a reinstall is not told anything through the device
    /// someone else signed in to after it.
    /// </summary>
    [Fact]
    public void AReinstalledDeviceIsNotToldWhatTheAccountBeforeCouldOpen()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        _db.AddDeviceToken(new DeviceToken { DeviceId = Guid.NewGuid(), Token = "reinstalled", UserId = alice });
        _db.AddDeviceToken(new DeviceToken { DeviceId = Guid.NewGuid(), Token = "reinstalled", UserId = bob });

        Assert.Empty(NotificationHelper.RecipientsWho(_db.GetAllDeviceTokens(), user => user == alice));
    }

    /// <summary>
    /// Two devices registering the same token at the same time leave one row between them.
    /// </summary>
    /// <remarks>
    /// Removing the other rows and writing this one are a single transaction. As two
    /// statements on their own, both registrations could remove nothing and then both
    /// write, which leaves the token with two owners again.
    /// </remarks>
    [Fact]
    public void TwoDevicesRegisteringOneTokenAtOnceLeaveOneRow()
    {
        for (var round = 0; round < 30; round++)
        {
            System.Threading.Tasks.Parallel.Invoke(
                () => _db.AddDeviceToken(new DeviceToken { DeviceId = Guid.NewGuid(), Token = "shared", UserId = Guid.NewGuid() }),
                () => _db.AddDeviceToken(new DeviceToken { DeviceId = Guid.NewGuid(), Token = "shared", UserId = Guid.NewGuid() }));

            Assert.Single(_db.GetAllDeviceTokens());
        }
    }

    /// <summary>
    /// A token stored on several rows before this version keeps only the newest one once
    /// the database is opened, and a token on one row is left as it is.
    /// </summary>
    [Fact]
    public void OpeningKeepsOnlyTheNewestRowOfAToken()
    {
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        var alone = Guid.NewGuid();
        Store(
            new DeviceToken { DeviceId = older, Token = "reinstalled", UserId = Guid.NewGuid(), Timestamp = 100 },
            new DeviceToken { DeviceId = newer, Token = "reinstalled", UserId = Guid.NewGuid(), Timestamp = 200 },
            new DeviceToken { DeviceId = alone, Token = "untouched", UserId = Guid.NewGuid(), Timestamp = 50 });

        var reopened = new PluginDatabase(_directory);

        Assert.Equal(
            new[] { alone, newer }.Order(),
            reopened.GetAllDeviceTokens().Select(t => t.DeviceId).Order());
    }

    /// <summary>
    /// Two rows of a token stamped at the same moment still leave exactly one.
    /// </summary>
    [Fact]
    public void OpeningKeepsOneRowOfATokenStampedTwiceAtOnce()
    {
        Store(
            new DeviceToken { DeviceId = Guid.NewGuid(), Token = "tied", UserId = Guid.NewGuid(), Timestamp = 300 },
            new DeviceToken { DeviceId = Guid.NewGuid(), Token = "tied", UserId = Guid.NewGuid(), Timestamp = 300 });

        var reopened = new PluginDatabase(_directory);

        Assert.Single(reopened.GetAllDeviceTokens());
    }

    /// <summary>
    /// The timestamp is written by the store, not by the caller.
    /// </summary>
    [Fact]
    public void RegisteringADeviceStampsIt()
    {
        var before = DateTime.UtcNow.ToFileTime();

        var token = _db.AddDeviceToken(new DeviceToken
        {
            DeviceId = Guid.NewGuid(),
            Token = "token",
            UserId = Guid.NewGuid(),
            Timestamp = 0
        });

        Assert.True(token.Timestamp >= before);
    }

    /// <summary>
    /// Distinct devices are kept apart.
    /// </summary>
    [Fact]
    public void DistinctDevicesArePersistedSeparately()
    {
        for (var i = 0; i < 5; i++)
        {
            _db.AddDeviceToken(new DeviceToken
            {
                DeviceId = Guid.NewGuid(),
                Token = "token" + i.ToString(CultureInfo.InvariantCulture),
                UserId = Guid.NewGuid()
            });
        }

        Assert.Equal(5, _db.TotalDevicesCount());
        Assert.Equal(5, _db.GetAllDeviceTokens().Count);
    }

    /// <summary>
    /// Tokens can be looked up by the user they belong to.
    /// </summary>
    [Fact]
    public void TokensAreFoundByUser()
    {
        var userId = Guid.NewGuid();

        _db.AddDeviceToken(new DeviceToken { DeviceId = Guid.NewGuid(), Token = "a", UserId = userId });
        _db.AddDeviceToken(new DeviceToken { DeviceId = Guid.NewGuid(), Token = "b", UserId = userId });
        _db.AddDeviceToken(new DeviceToken { DeviceId = Guid.NewGuid(), Token = "c", UserId = Guid.NewGuid() });

        Assert.Equal(2, _db.GetUserDeviceTokens(userId).Count);
    }

    /// <summary>
    /// Removing an unknown device is not an error.
    /// </summary>
    [Fact]
    public void RemovingAnUnknownDeviceDoesNothing()
    {
        _db.AddDeviceToken(new DeviceToken { DeviceId = Guid.NewGuid(), Token = "a", UserId = Guid.NewGuid() });

        _db.RemoveDeviceToken(Guid.NewGuid());

        Assert.Equal(1, _db.TotalDevicesCount());
    }

    /// <summary>
    /// Removing a known device forgets it.
    /// </summary>
    [Fact]
    public void RemovingADeviceForgetsIt()
    {
        var deviceId = Guid.NewGuid();
        _db.AddDeviceToken(new DeviceToken { DeviceId = deviceId, Token = "a", UserId = Guid.NewGuid() });

        _db.RemoveDeviceToken(deviceId);

        Assert.Null(_db.GetDeviceTokenForDeviceId(deviceId));
        Assert.Equal(0, _db.TotalDevicesCount());
    }

    /// <summary>
    /// Opening the same database twice applies migrations once and keeps the data.
    /// </summary>
    [Fact]
    public void ReopeningKeepsTheData()
    {
        var deviceId = Guid.NewGuid();
        _db.AddDeviceToken(new DeviceToken { DeviceId = deviceId, Token = "a", UserId = Guid.NewGuid() });

        var reopened = new PluginDatabase(_directory);

        Assert.NotNull(reopened.GetDeviceTokenForDeviceId(deviceId));
    }

    /// <summary>
    /// Writes rows as they are, the way a version before this one could have left them.
    /// </summary>
    private void Store(params DeviceToken[] rows)
    {
        using var context = _db.CreateContext();
        context.DeviceTokens.AddRange(rows);
        context.SaveChanges();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the test database.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            TestDirectory.Delete(_directory);
        }
    }
}

/// <summary>
/// Throwaway directories for database tests.
/// </summary>
internal static class TestDirectory
{
    /// <summary>
    /// Creates an empty directory nothing else is using.
    /// </summary>
    /// <returns>Its path.</returns>
    public static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "streamyfin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Deletes a directory created by <see cref="Create"/>.
    /// </summary>
    /// <param name="path">The directory.</param>
    /// <remarks>
    /// SQLite pools connections, so closing one is not enough: the pooled handle
    /// keeps the file open. Linux unlinks an open file without complaint, Windows
    /// throws. Drain the pool first, which is what made these tests pass off CI.
    /// </remarks>
    public static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
