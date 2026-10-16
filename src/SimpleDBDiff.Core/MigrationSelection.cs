using System.Collections.Frozen;

namespace SimpleDBDiff.Core;

public enum SchemaObjectKind { Table, Column, Constraint, Index }
public sealed record MigrationObjectKey(SchemaObjectKind Kind, string TableKey, string Name)
{
    public static MigrationObjectKey Table(string key) => new(SchemaObjectKind.Table, key, "");
}

public sealed class MigrationSelection(IEnumerable<MigrationObjectKey> objects)
{
    public IReadOnlySet<MigrationObjectKey> Objects { get; } = objects.ToFrozenSet();
    public bool Contains(SchemaObjectKind kind, string tableKey, string name) => Objects.Contains(new(kind, tableKey, name));
    public bool ContainsTable(string tableKey) => Objects.Contains(MigrationObjectKey.Table(tableKey));
}

public sealed class MigrationSelectionException(string message) : InvalidOperationException(message);

// Build the desired target from chosen source objects and retained, excluded target objects.
// A fully selected one-sided table is created/dropped as a whole. A partial selection changes only its chosen members.
public static class MigrationPlanner
{
    public static SchemaSnapshot Project(SchemaSnapshot source, SchemaSnapshot target, MigrationSelection selection)
    {
        var left = source.Tables.ToDictionary(t => t.Key, StringComparer.Ordinal);
        var right = target.Tables.ToDictionary(t => t.Key, StringComparer.Ordinal);
        var desired = new List<TableSchema>();
        foreach (var key in left.Keys.Union(right.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            left.TryGetValue(key, out var l); right.TryGetValue(key, out var r);
            if (l is null && selection.ContainsTable(key)) continue;
            var columns = Merge(l?.Columns ?? [], r?.Columns ?? [], c => c.Name, SchemaObjectKind.Column, key, selection);
            var constraints = Merge(l?.Constraints ?? [], r?.Constraints ?? [], c => c.Name, SchemaObjectKind.Constraint, key, selection);
            var indexes = Merge(l?.Indexes ?? [], r?.Indexes ?? [], i => i.Name, SchemaObjectKind.Index, key, selection);
            if (r is null && !selection.ContainsTable(key) && columns.Count == 0 && constraints.Count == 0 && indexes.Count == 0) continue;
            if (r is null && columns.Count == 0 && l!.Columns.Count > 0)
                throw new MigrationSelectionException($"Select at least one column to create table {key}, or uncheck that table and its objects.");
            var table = l ?? r!;
            desired.Add(new(table.Schema, table.Name, columns, constraints, indexes));
        }
        var snapshot = new SchemaSnapshot(desired);
        ValidateDependencies(snapshot);
        return snapshot;
    }

    private static IReadOnlyList<T> Merge<T>(IReadOnlyList<T> source, IReadOnlyList<T> target, Func<T, string> name, SchemaObjectKind kind, string tableKey, MigrationSelection selection)
    {
        var left = source.ToDictionary(name, StringComparer.Ordinal);
        var right = target.ToDictionary(name, StringComparer.Ordinal);
        var result = new List<T>();
        foreach (var key in left.Keys.Union(right.Keys, StringComparer.Ordinal))
        {
            var chosen = selection.Contains(kind, tableKey, key) ? left : right;
            if (chosen.TryGetValue(key, out var item)) result.Add(item);
        }
        return result;
    }

    private static void ValidateDependencies(SchemaSnapshot desired)
    {
        var tables = desired.Tables.ToDictionary(t => t.Key, StringComparer.Ordinal);
        foreach (var table in desired.Tables)
        {
            var columns = table.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            void Check(string type, string name, IReadOnlyList<string>? dependencies)
            {
                var missing = dependencies?.FirstOrDefault(c => !columns.Contains(c));
                if (missing is not null)
                    throw new MigrationSelectionException($"{type} {table.Key}.{name} needs column {missing}. Include that column, keep it on the target, or select the dependent object's removal.");
            }
            foreach (var column in table.Columns) Check("Column", column.Name, column.Dependencies);
            foreach (var index in table.Indexes) Check("Index", index.Name, index.Columns);
            foreach (var constraint in table.Constraints)
            {
                Check("Constraint", constraint.Name, constraint.Columns);
                if (constraint.ReferencedTable is not { } reference) continue;
                if (!tables.TryGetValue(reference, out var referenced))
                    throw new MigrationSelectionException($"Foreign key {table.Key}.{constraint.Name} needs table {reference}. Include that table, keep it on the target, or select the foreign key's removal.");
                var names = referenced.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
                var missing = constraint.ReferencedColumns?.FirstOrDefault(c => !names.Contains(c));
                if (missing is not null)
                    throw new MigrationSelectionException($"Foreign key {table.Key}.{constraint.Name} needs {reference}.{missing}. Include that column, keep it on the target, or select the foreign key's removal.");
                if (constraint.ReferencedKeyName is { } key &&
                    !(constraint.ReferencedKeyKind == SchemaObjectKind.Index ? referenced.Indexes.Any(i => i.Name == key) : referenced.Constraints.Any(c => c.Name == key)))
                    throw new MigrationSelectionException($"Foreign key {table.Key}.{constraint.Name} needs key {reference}.{key}. Include that key, keep it on the target, or select the foreign key's removal.");
            }
        }
    }
}
