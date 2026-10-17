using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SimpleDBDiff.Shared.Services;

public sealed class PasswordVault(IWorkspaceStorage storage) : IDisposable
{
    private byte[]? key;
    private Dictionary<string, string> secrets = [];
    private byte[]? salt;
    public bool IsOpen => key is not null;
    public async Task OpenAsync(string passphrase)
    {
        if (IsOpen) throw new InvalidOperationException("Close the vault first.");
        if (string.IsNullOrWhiteSpace(passphrase)) throw new ArgumentException("Enter the shared vault passphrase.");
        var saved = await storage.ReadAsync("vault-v1");
        var bytes = saved is null ? null : Convert.FromBase64String(saved);
        var nextSalt = bytes is null ? RandomNumberGenerator.GetBytes(16) : bytes[..16];
        var nextKey = Rfc2898DeriveBytes.Pbkdf2(passphrase, nextSalt, 310_000, HashAlgorithmName.SHA256, 32);
        try
        {
            var loaded = bytes is null ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(Decrypt(bytes, nextKey))!;
            if (saved is null)
            {
                var legacy = JsonSerializer.Deserialize<LegacyProfile[]>(await storage.ReadAsync("legacy-profiles") ?? "[]")!;
                foreach (var profile in legacy.Where(p => p.Secret is not null))
                {
                    var legacyKey = Rfc2898DeriveBytes.Pbkdf2(passphrase, profile.Secret!.AsSpan(0,16), 310_000, HashAlgorithmName.SHA256,32);
                    try { loaded[profile.Profile.Name] = Decrypt(profile.Secret!,legacyKey); }
                    finally { CryptographicOperations.ZeroMemory(legacyKey); }
                }
            }
            key = nextKey; salt = nextSalt; secrets = loaded;
            if (saved is null) await PersistAsync();
        }
        catch (CryptographicException) { Close(); CryptographicOperations.ZeroMemory(nextKey); throw new ArgumentException("The vault passphrase is incorrect or the vault is damaged."); }
        catch { Close(); CryptographicOperations.ZeroMemory(nextKey); throw; }
    }
    public string? Get(string name)
    {
        RequireOpen(); return secrets.GetValueOrDefault(name);
    }
    public async Task SetAsync(string name, string? password)
    {
        RequireOpen();
        var previous = new Dictionary<string, string>(secrets);
        if (password is null) secrets.Remove(name); else secrets[name] = password;
        try { await PersistAsync(); } catch { secrets = previous; throw; }
    }
    private async Task PersistAsync()
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(secrets);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var cipher = new byte[plain.Length];
            using var aes = new AesGcm(key!, 16); aes.Encrypt(nonce, plain, cipher, tag);
            await storage.WriteAsync("vault-v1", Convert.ToBase64String([.. salt!, .. nonce, .. tag, .. cipher]));
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private static string Decrypt(byte[] value, byte[] key)
    {
        if (value.Length < 44) throw new CryptographicException();
        var plain = new byte[value.Length - 44];
        try { using var aes = new AesGcm(key, 16); aes.Decrypt(value.AsSpan(16,12), value.AsSpan(44), value.AsSpan(28,16), plain); return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private void RequireOpen() { if (!IsOpen) throw new ArgumentException("Open the vault to save or retrieve passwords. You can still enter a password manually."); }
    public void Close() { if (key is not null) CryptographicOperations.ZeroMemory(key); key = null; salt = null; secrets.Clear(); }
    public void Dispose() => Close();
}
