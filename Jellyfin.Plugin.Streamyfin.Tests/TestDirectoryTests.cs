using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The throwaway directories every database test works in, and how they are cleaned up.
/// </summary>
public class TestDirectoryTests
{
    /// <summary>
    /// Deleting one test's directory leaves the connections of every other test's database
    /// in SQLite's pool. Test classes run in parallel: a cleanup that emptied every pool
    /// disposed a connection another class was opening at that moment, and that test
    /// failed with an <see cref="System.ObjectDisposedException"/> on
    /// <c>SQLitePCL.sqlite3</c>, once in a few dozen runs, on CI as on a laptop.
    /// </summary>
    [Fact]
    public void DeletingADirectoryLeavesTheOtherDatabasesPooled()
    {
        var mine = TestDirectory.Create();
        try
        {
            using var connection = new SqliteConnection($"Data Source={Path.Combine(mine, "pooled.db")}");
            connection.Open();
            var handle = connection.Handle;
            connection.Close();

            TestDirectory.Delete(TestDirectory.Create());

            connection.Open();
            Assert.Same(handle, connection.Handle);
        }
        finally
        {
            TestDirectory.Delete(mine);
        }
    }

    /// <summary>
    /// Deleting a directory empties the pools of its own databases, opened either way the
    /// plugin opens one: as EF Core does, and read only, as the import of the old database
    /// does. A connection left pooled keeps its file open, and Windows will not delete it.
    /// </summary>
    /// <param name="readOnly">Whether the connection is the read only kind.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletingADirectoryReleasesItsOwnDatabases(bool readOnly)
    {
        var path = TestDirectory.Create();
        var file = Path.Combine(path, "pooled.db");
        CreateDatabase(file);

        using var connection = new SqliteConnection(readOnly
            ? new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly }.ToString()
            : $"Data Source={file}");
        connection.Open();
        var handle = connection.Handle;
        connection.Close();

        TestDirectory.Delete(path);

        Directory.CreateDirectory(path);
        try
        {
            CreateDatabase(file);
            connection.Open();
            Assert.NotSame(handle, connection.Handle);
            connection.Close();
        }
        finally
        {
            TestDirectory.Delete(path);
        }
    }

    /// <summary>
    /// A directory whose database is still pooled is gone once deleted.
    /// </summary>
    [Fact]
    public void ADirectoryIsDeletedWithItsDatabaseStillPooled()
    {
        var path = TestDirectory.Create();
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(path, "pooled.db")}"))
        {
            connection.Open();
        }

        TestDirectory.Delete(path);

        Assert.False(Directory.Exists(path));
    }

    // A database with one table, written without a pool so that creating it leaves nothing
    // pooled behind.
    private static void CreateDatabase(string file)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "create table if not exists t (x integer)";
        command.ExecuteNonQuery();
    }
}
