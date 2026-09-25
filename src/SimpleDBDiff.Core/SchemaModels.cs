namespace SimpleDBDiff.Core;

public sealed record ColumnSchema(string Name, string DataType, bool IsNullable, string? DefaultExpression, bool IsIdentity = false, string? GeneratedExpression = null, string? IdentityGeneration = null);
public sealed record ConstraintSchema(string Name, char Kind, string Definition);
public sealed record IndexSchema(string Name, string Definition);
public sealed record TableSchema(string Schema, string Name, IReadOnlyList<ColumnSchema> Columns, IReadOnlyList<ConstraintSchema> Constraints, IReadOnlyList<IndexSchema> Indexes)
{
    public string Key => $"{Schema}.{Name}";
}
public sealed record SchemaSnapshot(IReadOnlyList<TableSchema> Tables);
public enum DifferenceKind { Equal, Different, LeftOnly, RightOnly }
public sealed record SchemaDifference(string ObjectType, string ObjectName, DifferenceKind Kind, string? LeftDefinition, string? RightDefinition);

public interface ISchemaReader
{
    Task<SchemaSnapshot> LoadAsync(string connectionString, CancellationToken cancellationToken = default);
}

public interface IMigrationGenerator
{
    string Generate(SchemaSnapshot leftSource, SchemaSnapshot rightTarget);
}
