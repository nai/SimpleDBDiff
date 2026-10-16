namespace SimpleDBDiff.Core;

public sealed record ColumnSchema(string Name, string DataType, bool IsNullable, string? DefaultExpression, bool IsIdentity = false, string? GeneratedExpression = null, string? IdentityGeneration = null, IReadOnlyList<string>? Dependencies = null);
public sealed record ConstraintSchema(string Name, char Kind, string Definition, IReadOnlyList<string>? Columns = null, string? ReferencedTable = null, IReadOnlyList<string>? ReferencedColumns = null, string? ReferencedKeyName = null, SchemaObjectKind? ReferencedKeyKind = null);
public sealed record IndexSchema(string Name, string Definition, IReadOnlyList<string>? Columns = null);
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
    string Generate(SchemaSnapshot leftSource, SchemaSnapshot rightTarget, MigrationSelection selection);
}
