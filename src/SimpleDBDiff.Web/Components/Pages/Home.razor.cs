using Npgsql;
using SimpleDBDiff.Core;
using SimpleDBDiff.Web.Services;

namespace SimpleDBDiff.Web.Components.Pages;

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
    private bool ShowEqual = true, ShowDifferent = true, ShowLeftOnly = true, ShowRightOnly = true;
    private readonly CancellationTokenSource Lifetime = new();
    private bool CanCompare => !string.IsNullOrWhiteSpace(Left.Database) && !string.IsNullOrWhiteSpace(Right.Database);
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
        try { Profiles = await ProfileStore.ListAsync(); }
        catch (Exception ex) { Logger.LogError(ex, "Failed to load connection profiles"); Error = "Saved profiles could not be loaded. You can still enter connection details."; }
    }

    private async Task LoadProfileAsync(ConnectionEditorState side)
    {
        try
        {
            var profile = Profiles.SingleOrDefault(p => p.Name == side.SelectedProfile)
                ?? throw new ArgumentException("Choose a saved profile first.");
            side.ProfileName = profile.Name;
            side.Host = profile.Host; side.Port = profile.Port; side.Database = profile.Database; side.Username = profile.Username;
            ConnectionChanged(side);
            side.Password = "";
            side.Password = profile.HasSavedPassword ? await ProfileStore.UnlockPasswordAsync(profile.Name, side.Passphrase) ?? "" : "";
            side.SavePassword = profile.HasSavedPassword;
            side.Notice = "Profile loaded. Ready to connect.";
        }
        catch (ArgumentException ex) { side.Error = ex.Message; }
        catch (Exception ex) { Logger.LogError(ex, "Failed to load connection profile"); side.Error = "Could not load the connection profile."; }
    }

    private async Task SaveProfileAsync(ConnectionEditorState side)
    {
        side.Error = null; side.Notice = null;
        try
        {
            await ProfileStore.SaveAsync(new(side.ProfileName, side.Host, side.Port, side.Database, side.Username, side.SavePassword), side.Password, side.Passphrase);
            Profiles = await ProfileStore.ListAsync(); side.SelectedProfile = side.ProfileName.Trim();
            side.Notice = "Profile saved.";
        }
        catch (ArgumentException ex) { side.Error = ex.Message; }
        catch (Exception ex) { Logger.LogError(ex, "Failed to save connection profile"); side.Error = "Could not save the connection profile."; }
    }

    private async Task LoadSingleAsync(ConnectionEditorState side)
    {
        Busy = true; ClearComparison(); Error = null;
        try { await LoadSchemaCoreAsync(side); }
        finally { Busy = false; RefreshTables(); }
    }

    private async Task LoadSchemaCoreAsync(ConnectionEditorState side)
    {
        side.Error = null; side.Notice = null; side.Snapshot = null;
        try
        {
            if (string.IsNullOrWhiteSpace(side.Host) || string.IsNullOrWhiteSpace(side.Database) || string.IsNullOrWhiteSpace(side.Username) || side.Port is < 1 or > 65535)
                throw new ArgumentException("Enter a host, valid port, database, and username.");
            var connection = new NpgsqlConnectionStringBuilder { Host = side.Host, Port = side.Port, Database = side.Database, Username = side.Username, Password = side.Password, Timeout = 10, CommandTimeout = 30 };
            side.Snapshot = await SchemaReader.LoadAsync(connection.ConnectionString, Lifetime.Token);
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

    private async Task CompareAsync()
    {
        Busy = true; Error = null; ClearComparison();
        try
        {
            await Task.WhenAll(LoadSchemaCoreAsync(Left), LoadSchemaCoreAsync(Right));
            if (Lifetime.IsCancellationRequested) return;
            if (Left.Snapshot is null || Right.Snapshot is null) { Error = "Both schemas must load successfully before comparison."; return; }
            var differences = SchemaComparer.Compare(Left.Snapshot, Right.Snapshot);
            Rows = ComparisonPresentation.Expand(Left.Snapshot, Right.Snapshot, differences);
            Selection = new(Rows);
            RefreshTables();
            SelectedTable = differences.FirstOrDefault(d => d.ObjectType == "Table" && d.Kind != DifferenceKind.Equal)?.ObjectName
                ?? TableEntries.FirstOrDefault()?.Key;
            ActiveTab = "details";
            Logger.LogInformation("Compared {LeftCount} source tables and {RightCount} target tables; found {ChangeCount} object changes", Left.Snapshot.Tables.Count, Right.Snapshot.Tables.Count, ChangeCount);
        }
        catch (Exception ex) { Logger.LogError(ex, "Comparison failed"); Error = "Comparison failed. Review schema metadata and server logs."; }
        finally { Busy = false; RefreshTables(); }
    }

    private void GenerateSql()
    {
        if (Rows is null || Selection is null || Left.Snapshot is null || Right.Snapshot is null) return;
        Script = null; Error = null; ActiveTab = "sql";
        try
        {
            Script = MigrationGenerator.Generate(Left.Snapshot, Right.Snapshot, Selection.Snapshot());
            Logger.LogInformation("Generated migration SQL for {SelectedChanges} selected changes", Selection.SelectedChangeCount);
        }
        catch (MigrationSelectionException ex) { Error = ex.Message; }
        catch (Exception ex) { Logger.LogError(ex, "SQL generation failed"); Error = "SQL generation failed. Review the selection and server logs."; }
    }

    private void SelectionChanged() { Script = null; Error = null; }
    private void ToggleTable(string key) { Selection?.SetTable(key, Selection.TableState(key) != true); SelectionChanged(); }
    private void SetObject(SchemaDifference row, bool include) { Selection?.SetObject(row, include); SelectionChanged(); }
    private void SetAllTables(bool include) { Selection?.SetAll(include); SelectionChanged(); }
    private void SetAllDetails(bool include) { if (SelectedTable is not null) Selection?.SetTable(SelectedTable, include); SelectionChanged(); }

    private void SelectTable(string key) { SelectedTable = key; ActiveTab = "details"; }
    private void Swap() { (Left, Right) = (Right, Left); ClearComparison(); Error = null; }
    private void ConnectionChanged(ConnectionEditorState side) { side.Snapshot = null; side.Error = null; side.Notice = null; Error = null; ClearComparison(); }
    private void ClearComparison() { Rows = null; Selection = null; Script = null; SelectedTable = null; ActiveTab = "details"; RefreshTables(); }
    public void Dispose() { Lifetime.Cancel(); Lifetime.Dispose(); }
    private sealed record TableEntry(string Key, DifferenceKind? Kind, TableDifferenceCounts Counts);
}
