using SimpleDBDiff.Shared.Services;

namespace SimpleDBDiff.Tests;

public sealed class VaultTests
{
    private sealed class MemoryStorage : IWorkspaceStorage
    {
        public readonly Dictionary<string, string> Data = [];
        public Task<string?> ReadAsync(string key) => Task.FromResult(Data.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value) { Data[key] = value; return Task.CompletedTask; }
    }
    [Fact]
    public async Task SharedPassphraseProtectsAllPasswordsAcrossInstancesAndLockPreventsAccess()
    {
        var storage = new MemoryStorage();
        using var vault = new PasswordVault(storage);
        await vault.OpenAsync("test-passphrase");
        await vault.SetAsync("left", "left-secret"); await vault.SetAsync("right", "right-secret");
        Assert.DoesNotContain("left-secret", storage.Data["vault-v1"]);
        vault.Close();
        Assert.Throws<ArgumentException>(() => vault.Get("left"));
        await Assert.ThrowsAsync<ArgumentException>(() => vault.SetAsync("left", "replacement"));
        await Assert.ThrowsAsync<ArgumentException>(() => vault.OpenAsync("wrong-passphrase"));
        Assert.False(vault.IsOpen);
        using var reopened = new PasswordVault(storage);
        await reopened.OpenAsync("test-passphrase");
        Assert.Equal("left-secret", reopened.Get("left")); Assert.Equal("right-secret", reopened.Get("right"));
    }
    [Fact]
    public async Task ProfilesSaveWithoutVaultUnlessPasswordIsRequested()
    {
        var storage = new MemoryStorage(); using var vault = new PasswordVault(storage);
        var profiles = new ConnectionProfileStore(storage, vault);
        var profile = new ConnectionProfile("source", "localhost", 5432, "demo", "postgres", false);
        await profiles.SaveAsync(profile, "manual-secret");
        Assert.Single(await profiles.ListAsync());
        Assert.DoesNotContain("manual-secret", storage.Data["profiles-v2"]);
        await Assert.ThrowsAsync<ArgumentException>(() => profiles.SaveAsync(profile with { HasSavedPassword = true }, "secret"));
    }
}
