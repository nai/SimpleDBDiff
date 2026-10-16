using Microsoft.Data.Sqlite;

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
