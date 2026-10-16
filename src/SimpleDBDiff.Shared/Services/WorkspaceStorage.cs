using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace SimpleDBDiff.Shared.Services;

public interface IWorkspaceStorage
{
    Task<string?> ReadAsync(string key);
    Task WriteAsync(string key, string value);
}

// Hosts choose the location; the shared UI never depends on a web content root.
public sealed class SqliteWorkspaceStorage(string path) : IWorkspaceStorage
{
    private async Task<SqliteConnection> OpenAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS workspace (key TEXT PRIMARY KEY, value TEXT NOT NULL)";
        await command.ExecuteNonQueryAsync();
        return connection;
    }
    public async Task<string?> ReadAsync(string key)
    {
        await using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        if (key == "legacy-profiles")
        {
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='profiles'";
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) == 0) return null;
            command.CommandText = "SELECT name, host, port, database_name, username, secret FROM profiles ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync();
            var profiles = new List<LegacyProfile>();
            while (await reader.ReadAsync())
            {
                var secret = reader.IsDBNull(5) ? null : (byte[])reader.GetValue(5);
                profiles.Add(new(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4), secret is not null), secret));
            }
            return JsonSerializer.Serialize(profiles);
        }
        command.CommandText = "SELECT value FROM workspace WHERE key=$key";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync() as string;
    }
    public async Task WriteAsync(string key, string value)
    {
        await using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO workspace VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed record LegacyProfile(ConnectionProfile Profile, byte[]? Secret);
