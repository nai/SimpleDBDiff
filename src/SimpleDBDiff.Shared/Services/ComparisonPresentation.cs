using SimpleDBDiff.Core;

namespace SimpleDBDiff.Shared.Services;

// One-sided tables have a single comparison result. Expand their metadata for the detail view.
public static class ComparisonPresentation
{
    public static IReadOnlyList<SchemaDifference> Expand(SchemaSnapshot left, SchemaSnapshot right, IReadOnlyList<SchemaDifference> differences)
    {
        var leftTables = left.Tables.ToDictionary(t => t.Key, StringComparer.Ordinal);
        var rightTables = right.Tables.ToDictionary(t => t.Key, StringComparer.Ordinal);
        var rows = differences.Select(d => d.ObjectType == "Table" ? d : d with
        {
            LeftDefinition = Definition(leftTables.GetValueOrDefault(TableKey(d)), d),
            RightDefinition = Definition(rightTables.GetValueOrDefault(TableKey(d)), d)
        }).ToList();
        foreach (var difference in differences.Where(d => d.ObjectType == "Table" && d.Kind is DifferenceKind.LeftOnly or DifferenceKind.RightOnly))
        {
            var isLeft = difference.Kind == DifferenceKind.LeftOnly;
            var table = (isLeft ? leftTables : rightTables)[difference.ObjectName];
            void Add(string type, string name, string definition) => rows.Add(new(type + " " + table.Key, name, difference.Kind, isLeft ? definition : null, isLeft ? null : definition));
            foreach (var column in table.Columns) Add("Column", column.Name, ColumnDefinition(column));
            foreach (var constraint in table.Constraints) Add("Constraint", constraint.Name, constraint.Definition);
            foreach (var index in table.Indexes) Add("Index", index.Name, index.Definition);
        }
        return rows;
    }

    private static string? Definition(TableSchema? table, SchemaDifference row) => row.ObjectType.Split(' ')[0] switch
    {
        "Column" => table?.Columns.FirstOrDefault(c => c.Name == row.ObjectName) is { } column ? ColumnDefinition(column) : null,
        "Constraint" => table?.Constraints.FirstOrDefault(c => c.Name == row.ObjectName)?.Definition,
        "Index" => table?.Indexes.FirstOrDefault(i => i.Name == row.ObjectName)?.Definition,
        _ => null
    };

    private static string ColumnDefinition(ColumnSchema column) => column.DataType +
        (column.GeneratedExpression is not null ? $" GENERATED ALWAYS AS ({column.GeneratedExpression}) STORED" :
            column.IsIdentity ? $" GENERATED {column.IdentityGeneration ?? "BY DEFAULT"} AS IDENTITY" :
            column.DefaultExpression is not null ? $" DEFAULT {column.DefaultExpression}" : "") +
        (column.IsNullable ? " NULL" : " NOT NULL");

    public static string TableKey(SchemaDifference difference) => difference.ObjectType == "Table"
        ? difference.ObjectName : difference.ObjectType[(difference.ObjectType.IndexOf(' ') + 1)..];

    public static MigrationObjectKey ObjectKey(SchemaDifference difference) => difference.ObjectType == "Table"
        ? MigrationObjectKey.Table(difference.ObjectName)
        : new(Enum.Parse<SchemaObjectKind>(difference.ObjectType.Split(' ')[0]), TableKey(difference), difference.ObjectName);

    public static TableDifferenceCounts Counts(IEnumerable<SchemaDifference> rows)
    {
        var all = rows.ToArray();
        var details = all.Where(d => d.ObjectType != "Table").ToArray();
        var counted = details.Length > 0 ? details : all;
        return new(counted.Count(d => d.Kind == DifferenceKind.Different), counted.Count(d => d.Kind == DifferenceKind.RightOnly), counted.Count(d => d.Kind == DifferenceKind.LeftOnly));
    }

    public static string StatusLabel(DifferenceKind? kind) => kind switch
    {
        DifferenceKind.Equal => "Equal", DifferenceKind.Different => "Different",
        DifferenceKind.LeftOnly => "Left only", DifferenceKind.RightOnly => "Right only", _ => "Not compared"
    };

    public static string StatusClass(DifferenceKind? kind) => kind switch
    {
        DifferenceKind.Equal => "equal", DifferenceKind.Different => "different",
        DifferenceKind.LeftOnly => "left-only", DifferenceKind.RightOnly => "right-only", _ => "pending"
    };
}

public sealed record TableDifferenceCounts(int Different, int RightOnly, int LeftOnly);
