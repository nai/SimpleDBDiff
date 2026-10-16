using System.Text;
using SimpleDBDiff.Core;

namespace SimpleDBDiff.Postgres;

public sealed class PostgresMigrationGenerator : IMigrationGenerator
{
    public string Generate(SchemaSnapshot leftSource, SchemaSnapshot rightTarget) => GenerateCore(leftSource, rightTarget, null);

    public string Generate(SchemaSnapshot leftSource, SchemaSnapshot rightTarget, MigrationSelection selection) =>
        GenerateCore(MigrationPlanner.Project(leftSource, rightTarget, selection), rightTarget, selection);

    private static string GenerateCore(SchemaSnapshot desired, SchemaSnapshot target, MigrationSelection? selection)
    {
        if (SchemaComparer.Compare(desired, target).All(d => d.Kind == DifferenceKind.Equal))
            return "-- Selected objects match. No migration is needed.";

        var left = desired.Tables.ToDictionary(t => t.Key, StringComparer.Ordinal);
        var right = target.Tables.ToDictionary(t => t.Key, StringComparer.Ordinal);
        var constraintDrops = new HashSet<MigrationObjectKey>();
        var indexDrops = new HashSet<MigrationObjectKey>();
        var replaced = right.ToDictionary(p => p.Key, p => p.Value.Columns.Where(c =>
            left.TryGetValue(p.Key, out var next) && next.Columns.Any(n => n.Name == c.Name && n.GeneratedExpression != c.GeneratedExpression))
            .Select(c => c.Name).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        HashSet<string> ChangedColumns(string key)
        {
            if (!right.TryGetValue(key, out var old)) return [];
            var next = left.GetValueOrDefault(key)?.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal) ?? [];
            return old.Columns.Where(c => !next.TryGetValue(c.Name, out var n) || n.DataType != c.DataType || n.GeneratedExpression != c.GeneratedExpression)
                .Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        }
        var changedColumns = right.Keys.ToDictionary(key => key, ChangedColumns, StringComparer.Ordinal);

        void RequireRebuildSelection(SchemaObjectKind kind, string tableKey, string name)
        {
            if (selection is not null && !selection.Contains(kind, tableKey, name) && !selection.ContainsTable(tableKey))
                throw new MigrationSelectionException($"Include {kind.ToString().ToLowerInvariant()} {tableKey}.{name} so it can be rebuilt for the selected column or key change, or exclude that change.");
        }

        bool ReferencedKeyChanges(ConstraintSchema fk)
        {
            if (fk.ReferencedTable is not { } reference || fk.ReferencedKeyName is not { } key || !right.TryGetValue(reference, out var old)) return false;
            if (!left.TryGetValue(reference, out var next)) return true;
            return fk.ReferencedKeyKind == SchemaObjectKind.Index
                ? !SameIndex(old.Indexes.FirstOrDefault(i => i.Name == key), next.Indexes.FirstOrDefault(i => i.Name == key))
                : !SameConstraint(old.Constraints.FirstOrDefault(c => c.Name == key), next.Constraints.FirstOrDefault(c => c.Name == key));
        }

        foreach (var old in target.Tables)
        {
            var next = left.GetValueOrDefault(old.Key);
            foreach (var constraint in old.Constraints)
            {
                if (next is null)
                {
                    // Drop outgoing foreign keys before any tables, including cycles between removed tables.
                    if (constraint.Kind == 'f') constraintDrops.Add(new(SchemaObjectKind.Constraint, old.Key, constraint.Name));
                    continue;
                }
                var replacement = next.Constraints.FirstOrDefault(c => c.Name == constraint.Name);
                var same = SameConstraint(constraint, replacement);
                var rebuild = (constraint.Columns ?? []).Any(replaced[old.Key].Contains);
                if (constraint.Kind == 'f')
                {
                    rebuild |= (constraint.Columns ?? []).Any(changedColumns[old.Key].Contains);
                    if (constraint.ReferencedTable is { } reference && changedColumns.TryGetValue(reference, out var changed))
                        rebuild |= (constraint.ReferencedColumns ?? []).Any(changed.Contains);
                    rebuild |= ReferencedKeyChanges(constraint);
                }
                if (same && rebuild) RequireRebuildSelection(SchemaObjectKind.Constraint, old.Key, constraint.Name);
                if (!same || rebuild) constraintDrops.Add(new(SchemaObjectKind.Constraint, old.Key, constraint.Name));
            }
            if (next is null) continue;
            foreach (var index in old.Indexes)
            {
                var same = SameIndex(index, next.Indexes.FirstOrDefault(i => i.Name == index.Name));
                var rebuild = (index.Columns ?? []).Any(replaced[old.Key].Contains);
                if (same && rebuild) RequireRebuildSelection(SchemaObjectKind.Index, old.Key, index.Name);
                if (!same || rebuild) indexDrops.Add(new(SchemaObjectKind.Index, old.Key, index.Name));
            }
        }

        var sql = new StringBuilder("-- Review this script and back up the target before running it.\nBEGIN;\n");
        foreach (var table in target.Tables.OrderBy(t => t.Key, StringComparer.Ordinal))
            foreach (var fk in table.Constraints.Where(c => c.Kind == 'f' && constraintDrops.Contains(new(SchemaObjectKind.Constraint, table.Key, c.Name))))
                sql.AppendLine($"ALTER TABLE {Q(table)} DROP CONSTRAINT {Q(fk.Name)};");

        foreach (var table in target.Tables.Where(t => !left.ContainsKey(t.Key)).OrderBy(t => t.Key, StringComparer.Ordinal))
            sql.AppendLine($"DROP TABLE {Q(table)};");
        foreach (var schema in desired.Tables.Where(t => !right.ContainsKey(t.Key)).Select(t => t.Schema).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            sql.AppendLine($"CREATE SCHEMA IF NOT EXISTS {Q(schema)};");

        foreach (var source in desired.Tables.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (!right.TryGetValue(source.Key, out var old))
            {
                sql.AppendLine($"CREATE TABLE {Q(source)} ({string.Join(", ", source.Columns.Select(ColumnDefinition))});");
                continue;
            }
            foreach (var index in old.Indexes.Where(i => indexDrops.Contains(new(SchemaObjectKind.Index, old.Key, i.Name))))
                sql.AppendLine($"DROP INDEX {Q(old.Schema)}.{Q(index.Name)};");
            foreach (var constraint in old.Constraints.Where(c => c.Kind != 'f' && constraintDrops.Contains(new(SchemaObjectKind.Constraint, old.Key, c.Name))))
                sql.AppendLine($"ALTER TABLE {Q(old)} DROP CONSTRAINT {Q(constraint.Name)};");

            var oldColumns = old.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
            var newColumns = source.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
            foreach (var column in old.Columns.Where(c => !newColumns.ContainsKey(c.Name) || replaced[old.Key].Contains(c.Name)))
                sql.AppendLine($"ALTER TABLE {Q(old)} DROP COLUMN {Q(column.Name)};");
            foreach (var column in source.Columns)
            {
                if (!oldColumns.TryGetValue(column.Name, out var previous) || replaced[old.Key].Contains(column.Name))
                {
                    sql.AppendLine($"ALTER TABLE {Q(source)} ADD COLUMN {ColumnDefinition(column)};");
                    continue;
                }
                var defaultChanged = SchemaComparer.Normalize(column.DefaultExpression) != SchemaComparer.Normalize(previous.DefaultExpression);
                var typeChanged = !string.Equals(SchemaComparer.Normalize(column.DataType), SchemaComparer.Normalize(previous.DataType), StringComparison.Ordinal);
                var dropDefaultFirst = previous.DefaultExpression is not null && ((typeChanged && defaultChanged) || (!previous.IsIdentity && column.IsIdentity));
                if (dropDefaultFirst) sql.AppendLine($"ALTER TABLE {Q(source)} ALTER COLUMN {Q(column.Name)} DROP DEFAULT;");
                if (typeChanged) sql.AppendLine($"ALTER TABLE {Q(source)} ALTER COLUMN {Q(column.Name)} TYPE {column.DataType} USING {Q(column.Name)}::{column.DataType};");
                if (column.IsIdentity != previous.IsIdentity)
                    sql.AppendLine($"ALTER TABLE {Q(source)} ALTER COLUMN {Q(column.Name)} {(column.IsIdentity ? $"ADD GENERATED {column.IdentityGeneration ?? "BY DEFAULT"} AS IDENTITY" : "DROP IDENTITY IF EXISTS")};");
                else if (column.IsIdentity && column.IdentityGeneration != previous.IdentityGeneration)
                    sql.AppendLine($"ALTER TABLE {Q(source)} ALTER COLUMN {Q(column.Name)} SET GENERATED {column.IdentityGeneration ?? "BY DEFAULT"};");
                if (defaultChanged && (column.DefaultExpression is not null || !dropDefaultFirst))
                    sql.AppendLine($"ALTER TABLE {Q(source)} ALTER COLUMN {Q(column.Name)} {(column.DefaultExpression is null ? "DROP DEFAULT" : $"SET DEFAULT {column.DefaultExpression}")};");
                if (column.IsNullable != previous.IsNullable)
                    sql.AppendLine($"ALTER TABLE {Q(source)} ALTER COLUMN {Q(column.Name)} {(column.IsNullable ? "DROP NOT NULL" : "SET NOT NULL")};");
            }
        }

        foreach (var table in desired.Tables.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var old = right.GetValueOrDefault(table.Key);
            foreach (var constraint in table.Constraints.Where(c => c.Kind != 'f' &&
                (!SameConstraint(c, old?.Constraints.FirstOrDefault(p => p.Name == c.Name)) || constraintDrops.Contains(new(SchemaObjectKind.Constraint, table.Key, c.Name)))))
                sql.AppendLine($"ALTER TABLE {Q(table)} ADD CONSTRAINT {Q(constraint.Name)} {constraint.Definition};");
            foreach (var index in table.Indexes.Where(i => !SameIndex(i, old?.Indexes.FirstOrDefault(p => p.Name == i.Name)) || indexDrops.Contains(new(SchemaObjectKind.Index, table.Key, i.Name))))
                sql.AppendLine(index.Definition.TrimEnd(';') + ";");
        }
        foreach (var table in desired.Tables.OrderBy(t => t.Key, StringComparer.Ordinal))
            foreach (var fk in table.Constraints.Where(c => c.Kind == 'f' &&
                (!SameConstraint(c, right.GetValueOrDefault(table.Key)?.Constraints.FirstOrDefault(p => p.Name == c.Name)) || constraintDrops.Contains(new(SchemaObjectKind.Constraint, table.Key, c.Name)))))
                sql.AppendLine($"ALTER TABLE {Q(table)} ADD CONSTRAINT {Q(fk.Name)} {fk.Definition};");
        sql.Append("COMMIT;\n");
        return sql.ToString();
    }

    private static bool SameConstraint(ConstraintSchema? left, ConstraintSchema? right) => left is not null && right is not null && left.Kind == right.Kind && SchemaComparer.Normalize(left.Definition) == SchemaComparer.Normalize(right.Definition);
    private static bool SameIndex(IndexSchema? left, IndexSchema? right) => left is not null && right is not null && SchemaComparer.Normalize(left.Definition) == SchemaComparer.Normalize(right.Definition);
    private static string ColumnDefinition(ColumnSchema c) => $"{Q(c.Name)} {c.DataType}" +
        (c.GeneratedExpression is not null ? $" GENERATED ALWAYS AS ({c.GeneratedExpression}) STORED" : c.IsIdentity ? $" GENERATED {c.IdentityGeneration ?? "BY DEFAULT"} AS IDENTITY" : c.DefaultExpression is not null ? $" DEFAULT {c.DefaultExpression}" : "") +
        (c.IsNullable ? "" : " NOT NULL");
    private static string Q(TableSchema table) => $"{Q(table.Schema)}.{Q(table.Name)}";
    private static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
