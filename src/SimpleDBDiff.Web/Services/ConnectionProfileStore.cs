using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SimpleDBDiff.Web.Services;

public sealed record ConnectionProfile(string Name, string Host, int Port, string Database, string Username, bool HasSavedPassword);

public sealed class ConnectionProfileStore(IWebHostEnvironment environment)
{
    private readonly string _path = Path.Combine(environment.ContentRootPath, ".data", "profiles.db");

    public async Task<IReadOnlyList<ConnectionProfile>> ListAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, host, port, database_name, username, secret IS NOT NULL FROM profiles ORDER BY name";
        await using var reader = await command.ExecuteReaderAsync();
        var profiles = new List<ConnectionProfile>();
        while (await reader.ReadAsync())
            profiles.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5)));
        return profiles;
    }

    public async Task SaveAsync(ConnectionProfile profile, string? password, string? passphrase)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.Host) ||
            string.IsNullOrWhiteSpace(profile.Database) || string.IsNullOrWhiteSpace(profile.Username) || profile.Port is < 1 or > 65535)
            throw new ArgumentException("A profile name, host, port, database, and username are required.");
        if (profile.HasSavedPassword && (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passphrase)))
            throw new ArgumentException("Enter a password and vault passphrase to save the password.");
        var secret = profile.HasSavedPassword ? Encrypt(password!, passphrase!) : null;
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO profiles (name, host, port, database_name, username, secret)
            VALUES ($name, $host, $port, $database, $username, $secret)
            ON CONFLICT(name) DO UPDATE SET host=excluded.host, port=excluded.port,
                database_name=excluded.database_name, username=excluded.username, secret=excluded.secret
            """;
        command.Parameters.AddWithValue("$name", profile.Name.Trim());
        command.Parameters.AddWithValue("$host", profile.Host.Trim());
        command.Parameters.AddWithValue("$port", profile.Port);
        command.Parameters.AddWithValue("$database", profile.Database.Trim());
        command.Parameters.AddWithValue("$username", profile.Username.Trim());
        command.Parameters.AddWithValue("$secret", (object?)secret ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<string?> UnlockPasswordAsync(string name, string passphrase)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT secret FROM profiles WHERE name = $name";
        command.Parameters.AddWithValue("$name", name);
        var value = await command.ExecuteScalarAsync();
        if (value is not byte[] bytes) return null;
        if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Enter the vault passphrase to unlock this password.");
        try { return Decrypt(bytes, passphrase); }
        catch (CryptographicException) { throw new ArgumentException("The vault passphrase is incorrect or the saved password is damaged."); }
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS profiles (name TEXT PRIMARY KEY, host TEXT NOT NULL, port INTEGER NOT NULL, database_name TEXT NOT NULL, username TEXT NOT NULL, secret BLOB NULL)";
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static byte[] Encrypt(string password, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, 310_000, HashAlgorithmName.SHA256, 32);
        var plain = Encoding.UTF8.GetBytes(password);
        var cipher = new byte[plain.Length]; var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
        CryptographicOperations.ZeroMemory(key);
        return [.. salt, .. nonce, .. tag, .. cipher];
    }

    private static string Decrypt(byte[] value, string passphrase)
    {
        if (value.Length < 44) throw new CryptographicException("Invalid secret.");
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, value.AsSpan(0, 16), 310_000, HashAlgorithmName.SHA256, 32);
        var plain = new byte[value.Length - 44];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(value.AsSpan(16, 12), value.AsSpan(44), value.AsSpan(28, 16), plain);
            return Encoding.UTF8.GetString(plain);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
}
