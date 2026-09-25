using System.Text;
using SimpleDBDiff.Core;

namespace SimpleDBDiff.Postgres;

public sealed class PostgresMigrationGenerator : IMigrationGenerator
{
    public string Generate(SchemaSnapshot leftSource, SchemaSnapshot rightTarget)
    {
        if (SchemaComparer.Compare(leftSource, rightTarget).All(d => d.Kind == DifferenceKind.Equal))
            return "-- Schemas match. No migration is needed.";

        var sql = new StringBuilder("-- Review this script and back up the target before running it.\nBEGIN;\n");
        var left = leftSource.Tables.ToDictionary(t => t.Key, StringComparer.Ordinal);
        var right = rightTarget.Tables.ToDictionary(t => t.Key, StringComparer.Ordinal);
        var all = left.Keys.Union(right.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal);

        // Foreign keys can reference tables and columns that are about to change.
        foreach (var table in rightTarget.Tables.OrderBy(t => t.Key, StringComparer.Ordinal))
            foreach (var fk in table.Constraints.Where(c => c.Kind == 'f'))
                sql.AppendLine($"ALTER TABLE {Q(table)} DROP CONSTRAINT {Q(fk.Name)};");

        foreach (var table in rightTarget.Tables.Where(t => !left.ContainsKey(t.Key)).OrderBy(t => t.Key, StringComparer.Ordinal))
            sql.AppendLine($"DROP TABLE {Q(table)};");

        foreach (var schema in leftSource.Tables.Where(t => !right.ContainsKey(t.Key)).Select(t => t.Schema).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            sql.AppendLine($"CREATE SCHEMA IF NOT EXISTS {Q(schema)};");

        foreach (var key in all)
        {
            if (!left.TryGetValue(key, out var source)) continue;
            if (!right.TryGetValue(key, out var target))
            {
                sql.AppendLine($"CREATE TABLE {Q(source)} ({string.Join(", ", source.Columns.Select(ColumnDefinition))});");
                continue;
            }

            var targetIndexes = target.Indexes.ToDictionary(i => i.Name, StringComparer.Ordinal);
            var sourceIndexes = source.Indexes.ToDictionary(i => i.Name, StringComparer.Ordinal);
            foreach (var index in target.Indexes.Where(i => !sourceIndexes.TryGetValue(i.Name, out var s) || SchemaComparer.Normalize(s.Definition) != SchemaComparer.Normalize(i.Definition)))
                sql.AppendLine($"DROP INDEX {Q(target.Schema)}.{Q(index.Name)};");

            var sourceConstraints = source.Constraints.Where(c => c.Kind != 'f').ToDictionary(c => c.Name, StringComparer.Ordinal);
            foreach (var constraint in target.Constraints.Where(c => c.Kind != 'f' && (!sourceConstraints.TryGetValue(c.Name, out var s) || s.Kind != c.Kind || SchemaComparer.Normalize(s.Definition) != SchemaComparer.Normalize(c.Definition))))
                sql.AppendLine($"ALTER TABLE {Q(target)} DROP CONSTRAINT {Q(constraint.Name)};");

            var targetColumns = target.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
            var sourceColumns = source.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
            foreach (var column in target.Columns.Where(c => !sourceColumns.ContainsKey(c.Name) || (sourceColumns[c.Name].GeneratedExpression != c.GeneratedExpression)))
                sql.AppendLine($"ALTER TABLE {Q(target)} DROP COLUMN {Q(column.Name)};");

            foreach (var column in source.Columns)
            {
                if (!targetColumns.TryGetValue(column.Name, out var old) || old.GeneratedExpression != column.GeneratedExpression)
                {
                    sql.AppendLine($"ALTER TABLE {Q(source)} ADD COLUMN {ColumnDefinition(column)};");
                    continue;
                }
                var defaultChanged = SchemaComparer.Normalize(column.DefaultExpression) != SchemaComparer.Normalize(old.DefaultExpression);
                var typeChanged = !string.Equals(SchemaComparer.Normalize(column.DataType), SchemaComparer.Normalize(old.DataType), StringComparison.Ordinal);
                var dropDefaultFirst = old.DefaultExpression is not null && ((typeChanged && defaultChanged) || (!old.IsIdentity && column.IsIdentity));
                if (dropDefaultFirst)
                    sql.AppendLine($"ALTER TABLE {Q(source)} ALTER COLUMN {Q(column.Name)} DROP DEFAULT;");
                if (typeChanged)
                    sql.AppendLine($"ALTER TABLE {Q(source)} ALTER COLUMN {Q(column.Name)} TYPE {column.DataType} USING {Q(column.Name)}::{column.DataType};");
                if (column.IsIdentity != old.IsIdentity)
                    sql.AppendLine($"ALTER TABLE {Q(source)} ALTER COLUMN {Q(column.Name)} {(column.IsIdentity ? $"ADD GENERATED {column.IdentityGeneration ?? "BY DEFAULT"} AS IDENTITY" : "DROP IDENTITY IF EXISTS")};");
                else if (column.IsIdentity && column.IdentityGeneration != old.IdentityGeneration)
                    sql.AppendLine($"ALTER TABLE {Q(source)} ALTER COLUMN {Q(column.Name)} SET GENERATED {column.IdentityGeneration ?? "BY DEFAULT"};");
                if (defaultChanged && (column.DefaultExpression is not null || !dropDefaultFirst))
                    sql.AppendLine($"ALTER TABLE {Q(source)} ALTER COLUMN {Q(column.Name)} {(column.DefaultExpression is null ? "DROP DEFAULT" : $"SET DEFAULT {column.DefaultExpression}")};");
                if (column.IsNullable != old.IsNullable)
                    sql.AppendLine($"ALTER TABLE {Q(source)} ALTER COLUMN {Q(column.Name)} {(column.IsNullable ? "DROP NOT NULL" : "SET NOT NULL")};");
            }
        }

        foreach (var table in leftSource.Tables.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            right.TryGetValue(table.Key, out var old);
            var oldConstraints = old?.Constraints.ToDictionary(c => c.Name, StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
            foreach (var constraint in table.Constraints.Where(c => c.Kind != 'f' && (!oldConstraints.TryGetValue(c.Name, out var previous) || previous.Kind != c.Kind || SchemaComparer.Normalize(previous.Definition) != SchemaComparer.Normalize(c.Definition))))
                sql.AppendLine($"ALTER TABLE {Q(table)} ADD CONSTRAINT {Q(constraint.Name)} {constraint.Definition};");
            var oldIndexes = old?.Indexes.ToDictionary(i => i.Name, StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
            foreach (var index in table.Indexes.Where(i => !oldIndexes.TryGetValue(i.Name, out var previous) || SchemaComparer.Normalize(previous.Definition) != SchemaComparer.Normalize(i.Definition)))
                sql.AppendLine(index.Definition.TrimEnd(';') + ";");
        }
        foreach (var table in leftSource.Tables.OrderBy(t => t.Key, StringComparer.Ordinal))
            foreach (var fk in table.Constraints.Where(c => c.Kind == 'f'))
                sql.AppendLine($"ALTER TABLE {Q(table)} ADD CONSTRAINT {Q(fk.Name)} {fk.Definition};");
        sql.Append("COMMIT;\n");
        return sql.ToString();
    }

    private static string ColumnDefinition(ColumnSchema c) =>
        $"{Q(c.Name)} {c.DataType}" +
        (c.GeneratedExpression is not null ? $" GENERATED ALWAYS AS ({c.GeneratedExpression}) STORED" : c.IsIdentity ? $" GENERATED {c.IdentityGeneration ?? "BY DEFAULT"} AS IDENTITY" : c.DefaultExpression is not null ? $" DEFAULT {c.DefaultExpression}" : "") +
        (c.IsNullable ? "" : " NOT NULL");

    private static string Q(TableSchema table) => $"{Q(table.Schema)}.{Q(table.Name)}";
    private static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
