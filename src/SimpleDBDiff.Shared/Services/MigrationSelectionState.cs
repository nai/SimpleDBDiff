using SimpleDBDiff.Core;

namespace SimpleDBDiff.Shared.Services;

public sealed class MigrationSelectionState
{
    private readonly IReadOnlyList<SchemaDifference> _rows;
    private readonly Dictionary<string, MigrationObjectKey[]> _members;
    private readonly HashSet<MigrationObjectKey> _selected = [];
    private readonly HashSet<string> _includedTables = new(StringComparer.Ordinal);

    public MigrationSelectionState(IReadOnlyList<SchemaDifference> rows)
    {
        _rows = rows;
        var grouped = rows.Where(d => d.ObjectType != "Table").GroupBy(ComparisonPresentation.TableKey)
            .ToDictionary(g => g.Key, g => g.Select(ComparisonPresentation.ObjectKey).ToArray(), StringComparer.Ordinal);
        _members = rows.Where(d => d.ObjectType == "Table").ToDictionary(d => d.ObjectName, d =>
        {
            var children = grouped.GetValueOrDefault(d.ObjectName) ?? [];
            return children.Length > 0 ? children : [MigrationObjectKey.Table(d.ObjectName)];
        }, StringComparer.Ordinal);
        foreach (var table in _members.Keys) SetDetails(table, true);
        SetAll(true);
    }

    public HashSet<MigrationObjectKey> NeedsReview { get; } = [];

    public SelectionCheckpoint Capture(SchemaSnapshot left, SchemaSnapshot right) => new(
        _rows.Select(row => new SelectionEntry(ComparisonPresentation.ObjectKey(row), Fingerprint(row, left, right), IsSelected(row))).ToArray(),
        _includedTables.ToArray());

    public void Restore(SelectionCheckpoint checkpoint, SchemaSnapshot left, SchemaSnapshot right)
    {
        var previous = checkpoint.Entries.ToDictionary(e => e.Key);
        _selected.Clear(); _includedTables.Clear(); NeedsReview.Clear();
        foreach (var row in _rows)
        {
            var key = ComparisonPresentation.ObjectKey(row);
            if (previous.TryGetValue(key, out var entry) && entry.Fingerprint == Fingerprint(row, left, right))
            { if (entry.Selected) _selected.Add(key); }
            else NeedsReview.Add(key);
        }
        foreach (var table in _members.Keys)
        {
            if (checkpoint.IncludedTables.Contains(table, StringComparer.Ordinal)) _includedTables.Add(table);
            UpdateTable(table);
        }
    }

    // Include dependency metadata as well as rendered definitions in refresh checks.
    private static string Fingerprint(SchemaDifference row, SchemaSnapshot left, SchemaSnapshot right)
    {
        object? Metadata(SchemaSnapshot snapshot)
        {
            var table = snapshot.Tables.FirstOrDefault(t => t.Key == ComparisonPresentation.TableKey(row));
            return row.ObjectType.Split(' ')[0] switch
            {
                "Column" => table?.Columns.FirstOrDefault(c => c.Name == row.ObjectName),
                "Constraint" => table?.Constraints.FirstOrDefault(c => c.Name == row.ObjectName),
                "Index" => table?.Indexes.FirstOrDefault(c => c.Name == row.ObjectName),
                _ => table
            };
        }
        return System.Text.Json.JsonSerializer.Serialize(new { Left = Metadata(left), Right = Metadata(right) });
    }

    public int SelectedObjectCount => _members.Where(t => _includedTables.Contains(t.Key)).Sum(t => t.Value.Count(_selected.Contains));
    public int SelectedChangeCount => _rows.Count(r => r.Kind != DifferenceKind.Equal &&
        (r.ObjectType != "Table" || _members[r.ObjectName][0].Kind == SchemaObjectKind.Table) && _includedTables.Contains(ComparisonPresentation.TableKey(r)) && IsSelected(r));
    public bool IsSelected(SchemaDifference row) => _selected.Contains(ComparisonPresentation.ObjectKey(row));
    public bool TableState(string key) => _includedTables.Contains(key);

    public void SetObject(SchemaDifference row, bool include)
    {
        var key = ComparisonPresentation.ObjectKey(row);
        if (include) _selected.Add(key); else _selected.Remove(key);
        UpdateTable(key.TableKey);
    }

    public void SetTable(string table, bool include)
    {
        if (include) _includedTables.Add(table); else _includedTables.Remove(table);
    }

    public void SetDetails(string table, bool include)
    {
        foreach (var member in _members[table])
            if (include) _selected.Add(member); else _selected.Remove(member);
        UpdateTable(table);
    }

    public void SetAll(bool include)
    {
        foreach (var key in _members.Keys) SetTable(key, include);
    }

    // Table inclusion gates the generated selection without changing remembered object choices.
    public MigrationSelection Snapshot() => new(_selected.Where(key => _includedTables.Contains(key.TableKey)));
    private void UpdateTable(string key)
    {
        var parent = MigrationObjectKey.Table(key);
        if (_members[key].All(_selected.Contains)) _selected.Add(parent); else _selected.Remove(parent);
    }
}

public sealed record SelectionEntry(MigrationObjectKey Key, string Fingerprint, bool Selected);
public sealed record SelectionCheckpoint(SelectionEntry[] Entries, string[] IncludedTables);
