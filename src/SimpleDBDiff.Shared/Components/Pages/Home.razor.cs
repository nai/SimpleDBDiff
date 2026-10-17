using Microsoft.Extensions.Logging;
using Npgsql;
using SimpleDBDiff.Core;
using SimpleDBDiff.Shared.Services;

namespace SimpleDBDiff.Shared.Components.Pages;

public partial class Home
{
    private ConnectionEditorState Left = new();
    private ConnectionEditorState Right = new();
    private IReadOnlyList<ConnectionProfile> Profiles = [];
    private IReadOnlyList<SchemaDifference>? Rows;
    private string? Script;
    private MigrationSelectionState? Selection;
    private string? Error;
    private string? SelectedTable;
    private string Search = "";
    private string ActiveTab = "details";
    private bool Busy;
    private bool VaultPanel;
    private bool SessionsOpen;
    private IReadOnlyList<ComparisonSession> Sessions = [];
    private ComparisonSession? ActiveSession;
    private ComparisonSession? PendingSession;
    private string SaveStatus = "No session loaded";
    private bool SaveFailed, Saving, Restoring;
    private string? LastSavedPayload, LastAttemptedPayload;
    private string? RenameId;
    private string RenameName = "";
    private string VaultPassphrase = "";
    private string? VaultError;
    private bool FiltersOpen;
    private bool ShowEqual = true, ShowDifferent = true, ShowLeftOnly = true, ShowRightOnly = true;
    private readonly CancellationTokenSource Lifetime = new();
    private bool CanCompare => Left.Snapshot is not null && Right.Snapshot is not null && !Left.IsStale && !Right.IsStale;
    private string ScriptDownload => "data:application/sql;charset=utf-8," + Uri.EscapeDataString(Script ?? "");
    private int ChangeCount => TableEntries.Sum(t => t.Counts.Different + t.Counts.LeftOnly + t.Counts.RightOnly);
    private DifferenceKind? SelectedKind => TableEntries.FirstOrDefault(t => t.Key == SelectedTable)?.Kind;
    private IEnumerable<SchemaDifference> FilteredRows => Rows?.Where(d => IsVisible(d.Kind)) ?? [];
    private IEnumerable<SchemaDifference> SelectedRows => FilteredRows.Where(d => d.ObjectType != "Table" && ComparisonPresentation.TableKey(d) == SelectedTable);

    private IReadOnlyList<TableEntry> TableEntries = [];

    private IEnumerable<TableEntry> VisibleTables
    {
        get
        {
            var visibleKeys = FilteredRows.Select(ComparisonPresentation.TableKey).ToHashSet(StringComparer.Ordinal);
            return TableEntries.Where(t => t.Key.Contains(Search ?? "", StringComparison.OrdinalIgnoreCase) &&
                (Rows is null || visibleKeys.Contains(t.Key)));
        }
    }

    private void RefreshTables()
    {
        var kinds = Rows?.Where(d => d.ObjectType == "Table").ToDictionary(d => d.ObjectName, d => (DifferenceKind?)d.Kind, StringComparer.Ordinal) ?? [];
        var counts = Rows?.Where(d => d.ObjectType != "Table").GroupBy(ComparisonPresentation.TableKey)
            .ToDictionary(g => g.Key, g => ComparisonPresentation.Counts(g), StringComparer.Ordinal) ?? [];
        TableEntries = (Left.Snapshot?.Tables.Select(t => t.Key) ?? [])
            .Union(Right.Snapshot?.Tables.Select(t => t.Key) ?? [], StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).Select(key => new TableEntry(key, kinds.GetValueOrDefault(key), counts.GetValueOrDefault(key)
                ?? ComparisonPresentation.Counts(Rows?.Where(d => d.ObjectType == "Table" && d.ObjectName == key) ?? []))).ToArray();
    }

    private bool IsVisible(DifferenceKind kind) => kind switch
    {
        DifferenceKind.Equal => ShowEqual, DifferenceKind.Different => ShowDifferent,
        DifferenceKind.LeftOnly => ShowLeftOnly, DifferenceKind.RightOnly => ShowRightOnly, _ => false
    };

    protected override async Task OnInitializedAsync()
    {
        try { Profiles = await ProfileStore.ListAsync(); Sessions = await SessionStore.ListAsync(); }
        catch (Exception ex) { Logger.LogError(ex, "Failed to load connection profiles"); Error = "Saved profiles could not be loaded. You can still enter connection details."; }
    }

    private async Task LoadProfileAsync(ConnectionEditorState side)
    {
        Busy = true;
        try
        {
            var profile = Profiles.SingleOrDefault(p => p.Name == side.SelectedProfile)
                ?? throw new ArgumentException("Choose a saved profile first.");
            side.ProfileName = profile.Name;
            side.Host = profile.Host; side.Port = profile.Port; side.Database = profile.Database; side.Username = profile.Username;
            ConnectionChanged(side);
            side.Password = "";
            side.Password = profile.HasSavedPassword && Vault.IsOpen ? await ProfileStore.UnlockPasswordAsync(profile.Name) ?? "" : "";
            side.SavePassword = profile.HasSavedPassword;
            side.Notice = profile.HasSavedPassword && !Vault.IsOpen ? "Profile loaded. Open the vault or enter the database password manually." : "Profile loaded. Ready to connect.";
        }
        catch (ArgumentException ex) { side.Error = ex.Message; }
        catch (Exception ex) { Logger.LogError(ex, "Failed to load connection profile"); side.Error = "Could not load the connection profile."; }
        finally { Busy = false; }
    }

    private async Task OpenVaultAsync()
    {
        Busy = true;
        try { await Vault.OpenAsync(VaultPassphrase); VaultPanel = false; VaultError = null; if (PendingSession is not null) await RetrieveSessionPasswordsAsync(); }
        catch (Exception ex) { VaultError = ex is ArgumentException ? ex.Message : "Could not open the vault."; Logger.LogWarning(ex, "Vault open failed"); }
        finally { VaultPassphrase = ""; Busy = false; }
    }
    private void CloseVault()
    {
        Vault.Close(); Left.Password = Right.Password = VaultPassphrase = ""; VaultPanel = false; VaultError = null;
    }
    private async Task SaveAndConnectAsync(ConnectionEditorState side)
    {
        Busy = true;
        try
        {
            await SaveProfileAsync(side);
            if (side.Error is not null) return;
            await LoadSingleAsync(side);
            side.Notice = side.Error is null ? "Profile saved. Connected and schema loaded." : "Profile saved. Connection failed; see error below.";
        }
        finally { Busy = false; }
    }
    private async Task TestAsync(ConnectionEditorState side)
    {
        Busy = true; side.Error = null; side.Notice = null;
        try { await using var connection = new NpgsqlConnection(ConnectionString(side)); await connection.OpenAsync(Lifetime.Token); side.Notice = "Connection successful."; }
        catch (Exception ex) { Logger.LogWarning(ex, "Connection test failed"); side.Error = "Connection test failed. Check the database, credentials, and permissions."; }
        finally { Busy = false; }
    }
    private static string ConnectionString(ConnectionEditorState side)
    {
        if (string.IsNullOrWhiteSpace(side.Host) || string.IsNullOrWhiteSpace(side.Database) || string.IsNullOrWhiteSpace(side.Username) || side.Port is < 1 or > 65535)
            throw new ArgumentException("Enter a host, valid port, database, and username.");
        return new NpgsqlConnectionStringBuilder { Host = side.Host, Port = side.Port, Database = side.Database, Username = side.Username, Password = side.Password, Timeout = 10, CommandTimeout = 30, Pooling = false }.ConnectionString;
    }

    private async Task SaveProfileAsync(ConnectionEditorState side)
    {
        side.Error = null; side.Notice = null;
        try
        {
            await ProfileStore.SaveAsync(new(side.ProfileName, side.Host, side.Port, side.Database, side.Username, side.SavePassword), side.Password);
            Profiles = await ProfileStore.ListAsync(); side.SelectedProfile = side.ProfileName.Trim();
            side.Notice = "Profile saved.";
        }
        catch (ArgumentException ex) { side.Error = ex.Message; }
        catch (Exception ex) { Logger.LogError(ex, "Failed to save connection profile"); side.Error = "Could not save the connection profile."; }
    }

    private async Task LoadSingleAsync(ConnectionEditorState side)
    {
        var checkpoint = Selection is not null && Left.Snapshot is not null && Right.Snapshot is not null
            ? Selection.Capture(Left.Snapshot, Right.Snapshot) : null;
        Busy = true; Script = null; Error = null;
        try
        {
            await LoadSchemaCoreAsync(side);
            if (CanCompare && Rows is not null) CompareLoaded(checkpoint);
        }
        finally { Busy = false; RefreshTables(); }
    }

    private async Task LoadSchemaCoreAsync(ConnectionEditorState side)
    {
        side.Error = null; side.Notice = null; side.IsStale = true;
        try
        {
            side.Snapshot = await SchemaReader.LoadAsync(ConnectionString(side), Lifetime.Token);
            side.IsStale = false; side.RefreshedAt = DateTimeOffset.Now;
            side.Notice = $"Connected to {side.Database}. {side.Snapshot.Tables.Count} tables loaded.";
            Logger.LogInformation("Loaded {TableCount} tables from {Host}/{Database}", side.Snapshot.Tables.Count, side.Host, side.Database);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is NpgsqlException or ArgumentException or TimeoutException or NotSupportedException)
        {
            Logger.LogWarning(ex, "Schema load failed for {Host}/{Database}", side.Host, side.Database);
            side.Error = ex is NotSupportedException or ArgumentException ? ex.Message : ex is PostgresException pg ? $"PostgreSQL error {pg.SqlState}: {pg.MessageText}" : "Could not connect or load schema. Check connection details and database permissions.";
        }
        catch (Exception ex) { Logger.LogError(ex, "Schema load failed"); side.Error = "Could not load this schema. Check the server logs for details."; }
    }

    private Task CompareAsync()
    {
        if (!CanCompare) { Error = "Connect both databases to load current schemas first."; return Task.CompletedTask; }
        var checkpoint = Selection?.Capture(Left.Snapshot!, Right.Snapshot!);
        CompareLoaded(checkpoint);
        return Task.CompletedTask;
    }

    private void CompareLoaded(SelectionCheckpoint? checkpoint = null)
    {
        Script = null; Error = null;
        try
        {
            var differences = SchemaComparer.Compare(Left.Snapshot!, Right.Snapshot!);
            Rows = ComparisonPresentation.Expand(Left.Snapshot!, Right.Snapshot!, differences);
            Selection = new(Rows);
            if (checkpoint is not null) Selection.Restore(checkpoint, Left.Snapshot!, Right.Snapshot!);
            RefreshTables();
            if (!TableEntries.Any(t => t.Key == SelectedTable))
                SelectedTable = differences.FirstOrDefault(d => d.ObjectType == "Table" && d.Kind != DifferenceKind.Equal)?.ObjectName ?? TableEntries.FirstOrDefault()?.Key;
            ActiveTab = "details";
        }
        catch (Exception ex) { ClearComparison(); Logger.LogError(ex, "Comparison failed"); Error = "Comparison failed. Review schema metadata and server logs."; }
    }

    private void GenerateSql()
    {
        if (!CanCompare || Rows is null || Selection is null) return;
        Script = null; Error = null; ActiveTab = "sql";
        try
        {
            Script = MigrationGenerator.Generate(Left.Snapshot!, Right.Snapshot!, Selection.Snapshot());
            Logger.LogInformation("Generated migration SQL for {SelectedChanges} selected changes", Selection.SelectedChangeCount);
        }
        catch (MigrationSelectionException ex) { Error = ex.Message; }
        catch (Exception ex) { Logger.LogError(ex, "SQL generation failed"); Error = "SQL generation failed. Review the selection and server logs."; }
    }

    private void SelectionChanged() { Script = null; Error = null; }
    private void ToggleTable(string key) { Selection?.SetTable(key, Selection.TableState(key) != true); SelectionChanged(); }
    private void SetObject(SchemaDifference row, bool include) { Selection?.SetObject(row, include); SelectionChanged(); }
    private void SetAllTables(bool include) { Selection?.SetAll(include); SelectionChanged(); }
    private void SetAllDetails(bool include) { if (SelectedTable is not null) Selection?.SetDetails(SelectedTable, include); SelectionChanged(); }

    private void SelectTable(string key) { SelectedTable = key; ActiveTab = "details"; }
    private void Swap() { (Left, Right) = (Right, Left); ClearComparison(); Error = null; if (CanCompare) CompareLoaded(); }
    private void ConnectionChanged(ConnectionEditorState side) { side.IsStale = side.Snapshot is not null; side.Error = null; side.Notice = null; Error = null; ClearComparison(); }
    private void ClearComparison() { Rows = null; Selection = null; Script = null; SelectedTable = null; ActiveTab = "details"; RefreshTables(); }
    public void Dispose() { Lifetime.Cancel(); Lifetime.Dispose(); CloseVault(); }
    private sealed record TableEntry(string Key, DifferenceKind? Kind, TableDifferenceCounts Counts);
}
