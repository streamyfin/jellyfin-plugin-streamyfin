using System;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.Streamyfin.Db;
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
