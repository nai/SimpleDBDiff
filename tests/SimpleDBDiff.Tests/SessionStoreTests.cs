using System.Text.Json;
using SimpleDBDiff.Shared.Services;
using SimpleDBDiff.Core;

namespace SimpleDBDiff.Tests;
public sealed class SessionStoreTests
{
    [Fact]
    public async Task LocalStorageRoundTripsSessionsSelectionsAndVaultWithoutPlaintextPasswords()
    {
        var directory = Path.Combine(Directory.GetCurrentDirectory(), ".data", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "workspace.db");
        try
        {
            var storage = new SqliteWorkspaceStorage(path);
            var editor = new ConnectionEditorState { Database = "source", Password = "never-in-session", SelectedProfile = "local" };
            var session = new ComparisonSession("one", "source → target", false, SessionConnection.From(editor), SessionConnection.From(editor),
                new("find", false, true, false, true), new([new(new(SchemaObjectKind.Column, "public.t", "id"), "metadata", false)], ["public.t"]), DateTimeOffset.UtcNow);
            var store = new ComparisonSessionStore(storage); await store.SaveAsync(session);
            var restored = Assert.Single(await new ComparisonSessionStore(new SqliteWorkspaceStorage(path)).ListAsync());
            Assert.Equal(session.Filters, restored.Filters);
            Assert.Equal(session.Selection!.Entries, restored.Selection!.Entries);
            Assert.Equal("", restored.Left.ToEditor().Password);
            Assert.DoesNotContain("never-in-session", (await storage.ReadAsync("sessions-v1"))!);
            using var vault = new PasswordVault(storage); await vault.OpenAsync("shared-passphrase"); await vault.SetAsync("local", editor.Password);
            vault.Close();
            using var reopened = new PasswordVault(new SqliteWorkspaceStorage(path)); await reopened.OpenAsync("shared-passphrase");
            Assert.Equal(editor.Password, reopened.Get("local"));
            await store.SaveAsync(restored with { Name = "Renamed", Renamed = true });
            Assert.Equal("Renamed", Assert.Single(await store.ListAsync()).Name);
            await store.DeleteAsync("one"); Assert.Empty(await store.ListAsync());
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory,true); }
    }
}
