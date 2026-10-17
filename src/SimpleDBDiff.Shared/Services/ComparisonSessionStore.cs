using System.Text.Json;

namespace SimpleDBDiff.Shared.Services;

// Explicit password-free DTOs prevent credentials or live snapshots entering session storage.
public sealed record SessionConnection(string ProfileName, string Host, int Port, string Database, string Username, bool SavePassword)
{
    public static SessionConnection From(ConnectionEditorState state) => new(state.SelectedProfile, state.Host, state.Port, state.Database, state.Username, state.SavePassword);
    public ConnectionEditorState ToEditor() => new() { SelectedProfile = ProfileName, ProfileName = ProfileName, Host = Host, Port = Port, Database = Database, Username = Username, SavePassword = SavePassword };
}
public sealed record SessionFilters(string Search, bool Equal, bool Different, bool LeftOnly, bool RightOnly);
public sealed record ComparisonSession(string Id, string Name, bool Renamed, SessionConnection Left, SessionConnection Right, SessionFilters Filters, SelectionCheckpoint? Selection, DateTimeOffset UpdatedAt);

public sealed class ComparisonSessionStore(IWorkspaceStorage storage)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<IReadOnlyList<ComparisonSession>> ListAsync() =>
        JsonSerializer.Deserialize<ComparisonSession[]>(await storage.ReadAsync("sessions-v1") ?? "[]")!;
    public async Task SaveAsync(ComparisonSession session)
    {
        await gate.WaitAsync();
        try
        {
            var sessions = (await ListAsync()).Where(s => s.Id != session.Id).Append(session).OrderByDescending(s => s.UpdatedAt).ToArray();
            await storage.WriteAsync("sessions-v1", JsonSerializer.Serialize(sessions));
        }
        finally { gate.Release(); }
    }
    public async Task DeleteAsync(string id)
    {
        await gate.WaitAsync();
        try { await storage.WriteAsync("sessions-v1", JsonSerializer.Serialize((await ListAsync()).Where(s => s.Id != id))); }
        finally { gate.Release(); }
    }
}
