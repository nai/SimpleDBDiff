using System.Text.Json;

namespace SimpleDBDiff.Shared.Services;

public sealed record ConnectionProfile(string Name, string Host, int Port, string Database, string Username, bool HasSavedPassword);

public sealed class ConnectionProfileStore(IWorkspaceStorage storage, PasswordVault vault)
{
    public async Task<IReadOnlyList<ConnectionProfile>> ListAsync()
    {
        var saved = await storage.ReadAsync("profiles-v2");
        if (saved is not null) return JsonSerializer.Deserialize<ConnectionProfile[]>(saved)!;
        return (JsonSerializer.Deserialize<LegacyProfile[]>(await storage.ReadAsync("legacy-profiles") ?? "[]")!).Select(p => p.Profile).ToArray();
    }

    public async Task SaveAsync(ConnectionProfile profile, string password)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.Host) || string.IsNullOrWhiteSpace(profile.Database) || string.IsNullOrWhiteSpace(profile.Username) || profile.Port is < 1 or > 65535)
            throw new ArgumentException("A profile name, host, valid port, database, and username are required.");
        profile = profile with { Name = profile.Name.Trim() };
        var profiles = (await ListAsync()).Where(p => p.Name != profile.Name).Append(profile).OrderBy(p => p.Name).ToArray();
        if (profile.HasSavedPassword) await vault.SetAsync(profile.Name, password);
        await storage.WriteAsync("profiles-v2", JsonSerializer.Serialize(profiles));
    }
    public Task<string?> UnlockPasswordAsync(string name) => Task.FromResult(vault.Get(name));
}
