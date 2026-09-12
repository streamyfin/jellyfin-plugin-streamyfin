using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Streamyfin.Db;

/// <summary>
/// Reads device tokens out of the hand written SQLite store that EF Core replaced.
/// </summary>
/// <remarks>
/// This is the only place left that talks to SQLite directly, and it exists to read
/// a schema EF Core does not model. It opens the file read only, so a downgrade back
/// to an older plugin build still finds its database exactly as it left it. Delete
/// this class the day the old file stops being worth importing.
/// </remarks>
internal static class LegacyDeviceTokenReader
{
    private const string TableName = "device_tokens";

    /// <summary>
    /// Reads every device token from the old database.
    /// </summary>
    /// <param name="dbFilePath">Path to the old database file.</param>
    /// <returns>The tokens it held, empty when the table is missing.</returns>
    public static List<DeviceToken> Read(string dbFilePath)
    {
        var tokens = new List<DeviceToken>();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbFilePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        if (!TableExists(connection))
        {
            return tokens;
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"select DeviceId, Token, UserId, Timestamp from {TableName}";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // The old store wrote GUIDs through Microsoft.Data.Sqlite's own mapping,
            // so they come back as text rather than as blobs.
            tokens.Add(new DeviceToken
            {
                DeviceId = reader.GetGuid(0),
                Token = reader.GetString(1),
                UserId = reader.GetGuid(2),
                Timestamp = reader.GetInt64(3)
            });
        }

        return tokens;
    }

    private static bool TableExists(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select 1 from sqlite_master where type = 'table' and name = $name limit 1";
        command.Parameters.AddWithValue("$name", TableName);

        return command.ExecuteScalar() is not null;
    }
}
