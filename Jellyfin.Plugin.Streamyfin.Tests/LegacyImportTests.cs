using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.Streamyfin.Db;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The one time import that carries device tokens over from the hand written store.
/// </summary>
/// <remarks>
/// The rule the tests exist to hold: an admin who installs this build keeps their
/// registered devices, and an admin who then downgrades finds the old database
/// exactly as they left it.
/// </remarks>
public class LegacyImportTests : IDisposable
{
    private readonly string _directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="LegacyImportTests"/> class.
    /// </summary>
    public LegacyImportTests()
    {
        _directory = TestDirectory.Create();
    }

    /// <summary>
    /// Tokens written by the old store are carried over on first start.
    /// </summary>
    [Fact]
    public void TokensAreCarriedOver()
    {
        var deviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        WriteLegacyDatabase([(deviceId, "legacy-token", userId, 132_000_000_000_000_000L)]);

        var db = new PluginDatabase(_directory);
        var imported = db.GetDeviceTokenForDeviceId(deviceId);

        Assert.NotNull(imported);
        Assert.Equal("legacy-token", imported.Token);
        Assert.Equal(userId, imported.UserId);
        Assert.Equal(132_000_000_000_000_000L, imported.Timestamp);
    }

    /// <summary>
    /// The old file is opened read only and left untouched, so a downgrade still works.
    /// </summary>
    [Fact]
    public void TheOldDatabaseIsLeftAlone()
    {
        WriteLegacyDatabase([(Guid.NewGuid(), "legacy-token", Guid.NewGuid(), 1L)]);

        var legacyPath = Path.Combine(_directory, PluginDatabase.LegacyFileName);
        var before = new FileInfo(legacyPath).Length;

        _ = new PluginDatabase(_directory);

        Assert.True(File.Exists(legacyPath));
        Assert.Equal(before, new FileInfo(legacyPath).Length);
        Assert.Single(LegacyDeviceTokenReader.Read(legacyPath));
    }

    /// <summary>
    /// Starting twice does not import twice.
    /// </summary>
    [Fact]
    public void TheImportRunsOnlyOnce()
    {
        var deviceId = Guid.NewGuid();
        WriteLegacyDatabase([(deviceId, "legacy-token", Guid.NewGuid(), 1L)]);

        var first = new PluginDatabase(_directory);
        first.RemoveDeviceToken(deviceId);

        var second = new PluginDatabase(_directory);

        // If the import ran again it would put the token back.
        Assert.Equal(0, second.TotalDevicesCount());
    }

    /// <summary>
    /// A fresh install with no old database still starts, and does not keep
    /// looking for a file that will never appear.
    /// </summary>
    [Fact]
    public void NoOldDatabaseIsNotAnError()
    {
        var db = new PluginDatabase(_directory);

        Assert.Equal(0, db.TotalDevicesCount());

        using var context = db.CreateContext();
        Assert.Single(context.ImportMarkers);
    }

    /// <summary>
    /// A corrupt old database does not stop the plugin from starting, and the
    /// import is left pending so the next start can try again.
    /// </summary>
    [Fact]
    public void ACorruptOldDatabaseDoesNotBreakStartup()
    {
        File.WriteAllText(Path.Combine(_directory, PluginDatabase.LegacyFileName), "this is not a database");

        var db = new PluginDatabase(_directory);

        Assert.Equal(0, db.TotalDevicesCount());

        using var context = db.CreateContext();
        Assert.Empty(context.ImportMarkers);
    }

    /// <summary>
    /// An old database whose table is missing imports nothing and completes.
    /// </summary>
    [Fact]
    public void AnEmptyOldDatabaseImportsNothing()
    {
        var legacyPath = Path.Combine(_directory, PluginDatabase.LegacyFileName);
        using (var connection = new SqliteConnection($"Data Source={legacyPath}"))
        {
            connection.Open();
        }

        var db = new PluginDatabase(_directory);

        Assert.Equal(0, db.TotalDevicesCount());

        using var context = db.CreateContext();
        Assert.Single(context.ImportMarkers);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the test directory.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            TestDirectory.Delete(_directory);
        }
    }

    /// <summary>
    /// Writes a database in the shape the hand written store used.
    /// </summary>
    /// <param name="rows">The tokens it should hold.</param>
    private void WriteLegacyDatabase(IEnumerable<(Guid DeviceId, string Token, Guid UserId, long Timestamp)> rows)
    {
        var path = Path.Combine(_directory, PluginDatabase.LegacyFileName);

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText =
                "create table if not exists device_tokens "
                + "(DeviceId GUID PRIMARY KEY, Token TEXT NOT NULL, UserId GUID NOT NULL, Timestamp INTEGER NOT NULL)";
            create.ExecuteNonQuery();
        }

        foreach (var row in rows)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText =
                "insert into device_tokens(DeviceId, Token, UserId, Timestamp) "
                + "values ($deviceId, $token, $userId, $timestamp)";
            insert.Parameters.AddWithValue("$deviceId", row.DeviceId);
            insert.Parameters.AddWithValue("$token", row.Token);
            insert.Parameters.AddWithValue("$userId", row.UserId);
            insert.Parameters.AddWithValue("$timestamp", row.Timestamp);
            insert.ExecuteNonQuery();
        }
    }
}
