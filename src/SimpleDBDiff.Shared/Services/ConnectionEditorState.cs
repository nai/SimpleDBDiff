using SimpleDBDiff.Core;

namespace SimpleDBDiff.Shared.Services;

public sealed class ConnectionEditorState
{
    public string SelectedProfile { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "";
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = "";
    public bool IsStale { get; set; }
    public DateTimeOffset? RefreshedAt { get; set; }
    public bool SavePassword { get; set; }
    public SchemaSnapshot? Snapshot { get; set; }
    public string? Error { get; set; }
    public string? Notice { get; set; }
}
