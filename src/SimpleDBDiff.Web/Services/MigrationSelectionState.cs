using SimpleDBDiff.Core;

namespace SimpleDBDiff.Web.Services;

public sealed class MigrationSelectionState
{
    private readonly IReadOnlyList<SchemaDifference> _rows;
    private readonly Dictionary<string, MigrationObjectKey[]> _members;
    private readonly HashSet<MigrationObjectKey> _selected = [];

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
        SetAll(true);
    }

    public int SelectedObjectCount => _members.Values.Sum(m => m.Count(_selected.Contains));
    public int SelectedChangeCount => _rows.Count(r => r.Kind != DifferenceKind.Equal &&
        (r.ObjectType != "Table" || _members[r.ObjectName][0].Kind == SchemaObjectKind.Table) && IsSelected(r));
    public bool IsSelected(SchemaDifference row) => _selected.Contains(ComparisonPresentation.ObjectKey(row));
    public bool? TableState(string key)
    {
        var members = _members[key];
        var count = members.Count(_selected.Contains);
        return count == 0 ? false : count == members.Length ? true : null;
    }

    public void SetObject(SchemaDifference row, bool include)
    {
        var key = ComparisonPresentation.ObjectKey(row);
        if (include) _selected.Add(key); else _selected.Remove(key);
        UpdateTable(key.TableKey);
    }

    public void SetTable(string table, bool include)
    {
        foreach (var member in _members[table])
            if (include) _selected.Add(member); else _selected.Remove(member);
        UpdateTable(table);
    }

    public void SetAll(bool include)
    {
        foreach (var key in _members.Keys) SetTable(key, include);
    }

    public MigrationSelection Snapshot() => new(_selected);
    private void UpdateTable(string key)
    {
        var parent = MigrationObjectKey.Table(key);
        if (_members[key].All(_selected.Contains)) _selected.Add(parent); else _selected.Remove(parent);
    }
}
