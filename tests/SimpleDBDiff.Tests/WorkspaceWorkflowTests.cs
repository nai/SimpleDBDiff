using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using SimpleDBDiff.Core;
using SimpleDBDiff.Shared.Components.Pages;
using SimpleDBDiff.Shared.Services;

namespace SimpleDBDiff.Tests;
public sealed class WorkspaceWorkflowTests
{
    private sealed class Reader : ISchemaReader
    {
        public int Calls;
        public bool Fail;
        public Task<SchemaSnapshot> LoadAsync(string connectionString, CancellationToken cancellationToken = default)
        { Calls++; if (Fail) throw new ArgumentException("Connection failed for test."); return Task.FromResult(new SchemaSnapshot([])); }
    }
    private sealed class Storage : IWorkspaceStorage
    {
        private readonly Dictionary<string,string> data = [];
        public Task<string?> ReadAsync(string key) => Task.FromResult(data.GetValueOrDefault(key));
        public Task WriteAsync(string key,string value) { data[key]=value; return Task.CompletedTask; }
    }
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static T Field<T>(Home home,string name) => (T)typeof(Home).GetField(name,Flags)!.GetValue(home)!;
    private static void Set(Home home,string name,object value) => typeof(Home).GetField(name,Flags)!.SetValue(home,value);
    private static object? Call(Home home,string method, params object[] args) => typeof(Home).GetMethod(method,Flags)!.Invoke(home,args);
    private static void Inject(Home home,string name,object value) => typeof(Home).GetProperty(name,Flags)!.SetValue(home,value);
    [Fact]
    public async Task CompareUsesLoadedSnapshotsAndVaultCloseKeepsResultsWhileEditsRequireReconnect()
    {
        var home = new Home(); var reader = new Reader(); var storage = new Storage(); using var vault = new PasswordVault(storage);
        Inject(home,"SchemaReader",reader); Inject(home,"Vault",vault); Inject(home,"Logger",NullLogger<Home>.Instance);
        var left = Field<ConnectionEditorState>(home,"Left"); var right = Field<ConnectionEditorState>(home,"Right");
        left.Snapshot = right.Snapshot = new SchemaSnapshot([]); left.Password = right.Password = "manual";
        await (Task)Call(home,"CompareAsync")!;
        Assert.Equal(0,reader.Calls); var rows = Field<object>(home,"Rows");
        Call(home,"CloseVault"); Assert.Same(rows,Field<object>(home,"Rows")); Assert.Empty(left.Password); Assert.Empty(right.Password);
        Set(home,"Script","old SQL"); Call(home,"ConnectionChanged",left);
        Assert.True(left.IsStale); Assert.NotNull(left.Snapshot); Assert.Null(Field<string?>(home,"Script"));
        await (Task)Call(home,"CompareAsync")!; Assert.Equal(0,reader.Calls); Assert.Contains("Connect both",Field<string>(home,"Error"));
        home.Dispose();
    }
    [Fact]
    public async Task SaveAndConnectReportsSavedProfileWhenConnectionFails()
    {
        var home = new Home(); var storage = new Storage(); using var vault = new PasswordVault(storage);
        var profiles = new ConnectionProfileStore(storage,vault);
        Inject(home,"SchemaReader",new Reader { Fail = true }); Inject(home,"ProfileStore",profiles); Inject(home,"Vault",vault); Inject(home,"Logger",NullLogger<Home>.Instance);
        var left = Field<ConnectionEditorState>(home,"Left"); left.ProfileName = "local"; left.Database = "demo";
        await (Task)Call(home,"SaveAndConnectAsync",left)!;
        Assert.Single(await profiles.ListAsync()); Assert.Contains("Profile saved. Connection failed",left.Notice); Assert.NotNull(left.Error);
        home.Dispose();
    }
}
