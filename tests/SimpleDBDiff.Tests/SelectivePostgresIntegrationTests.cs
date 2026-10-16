using Npgsql;
using SimpleDBDiff.Core;

namespace SimpleDBDiff.Tests;

public sealed class SelectivePostgresIntegrationTests : IAsyncLifetime
{
    private readonly Postgres.PostgresSchemaReader _reader = new();
    private readonly Postgres.PostgresMigrationGenerator _generator = new();
    private static string? Left => Environment.GetEnvironmentVariable("SIMPLEDBDIFF_TEST_LEFT");
    private static string? Right => Environment.GetEnvironmentVariable("SIMPLEDBDIFF_TEST_RIGHT");

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (Left is not null) await ExecuteAsync(Left, "DROP SCHEMA IF EXISTS sdd_select CASCADE");
        if (Right is not null) await ExecuteAsync(Right, "DROP SCHEMA IF EXISTS sdd_select CASCADE");
    }

    [Fact]
    public async Task SelectiveSqlAppliesChosenChangesAndPreservesExcludedTablesColumnsIndexesAndForeignKeys()
    {
        if (Left is null || Right is null) return;
        await ResetAsync(Left, """
            CREATE TABLE sdd_select.parent (id integer PRIMARY KEY);
            CREATE TABLE sdd_select.items (id integer PRIMARY KEY, parent_id integer REFERENCES sdd_select.parent(id), label text NOT NULL DEFAULT 'new', source_only integer, keep integer);
            CREATE INDEX items_label_idx ON sdd_select.items(label);
            CREATE TABLE sdd_select.left_only (keep text, excluded integer);
            """);
        await ResetAsync(Right, """
            CREATE TABLE sdd_select.parent (id integer PRIMARY KEY);
            CREATE TABLE sdd_select.items (id integer PRIMARY KEY, parent_id integer REFERENCES sdd_select.parent(id), label varchar(20) DEFAULT 'old', target_only integer, keep integer);
            CREATE INDEX items_keep_idx ON sdd_select.items(keep);
            CREATE TABLE sdd_select.right_only (id integer PRIMARY KEY);
            """);
        var source = await LoadAsync(Left); var target = await LoadAsync(Right);
        Assert.DoesNotContain(source.Tables.SelectMany(t => t.Constraints), c => c.Kind == 'n');
        var selection = new MigrationSelection([
            new(SchemaObjectKind.Column, "sdd_select.items", "label"),
            new(SchemaObjectKind.Column, "sdd_select.items", "source_only"),
            new(SchemaObjectKind.Column, "sdd_select.left_only", "keep")]);
        var sql = _generator.Generate(source, target, selection);
        Assert.DoesNotContain("items_parent_id_fkey", sql);
        Assert.DoesNotContain("right_only", sql);
        Assert.DoesNotContain("target_only", sql);
        Assert.DoesNotContain("items_keep_idx", sql);
        Assert.DoesNotContain("excluded", sql);
        await ExecuteAsync(Right, sql);
        var after = await LoadAsync(Right);
        var items = after.Tables.Single(t => t.Name == "items");
        var label = items.Columns.Single(c => c.Name == "label");
        Assert.Equal("text", label.DataType); Assert.False(label.IsNullable); Assert.Equal("'new'::text", label.DefaultExpression);
        Assert.Contains(items.Columns, c => c.Name == "target_only");
        Assert.Contains(items.Columns, c => c.Name == "source_only");
        Assert.Contains(items.Indexes, i => i.Name == "items_keep_idx");
        Assert.Contains(items.Constraints, c => c.Kind == 'f');
        Assert.Contains(after.Tables, t => t.Name == "right_only");
        Assert.Equal("keep", Assert.Single(after.Tables.Single(t => t.Name == "left_only").Columns).Name);
    }

    [Fact]
    public async Task ExcludedDependenciesFailClearlyAndSelectingTheirRemovalsProducesExecutableSql()
    {
        if (Left is null || Right is null) return;
        await ResetAsync(Left, """
            CREATE TABLE sdd_select.items (keep integer);
            CREATE TABLE sdd_select.child (id integer PRIMARY KEY, item_id integer);
            """);
        await ResetAsync(Right, """
            CREATE TABLE sdd_select.items (keep integer, obsolete integer CHECK (obsolete > 0));
            CREATE UNIQUE INDEX items_obsolete_key ON sdd_select.items(obsolete);
            CREATE INDEX items_predicate_idx ON sdd_select.items(keep) WHERE obsolete > 0;
            CREATE TABLE sdd_select.child (id integer PRIMARY KEY, item_id integer REFERENCES sdd_select.items(obsolete));
            """);
        var source = await LoadAsync(Left); var target = await LoadAsync(Right);
        var items = target.Tables.Single(t => t.Name == "items");
        Assert.Contains(items.Indexes, i => i.Name == "items_obsolete_key");
        Assert.Contains("obsolete", items.Indexes.Single(i => i.Name == "items_predicate_idx").Columns!);
        var fk = target.Tables.Single(t => t.Name == "child").Constraints.Single(c => c.Kind == 'f');
        Assert.Equal("sdd_select.items", fk.ReferencedTable);
        Assert.Equal("items_obsolete_key", fk.ReferencedKeyName);
        Assert.Equal(SchemaObjectKind.Index, fk.ReferencedKeyKind);
        var keys = new List<MigrationObjectKey> { new(SchemaObjectKind.Column, items.Key, "obsolete") };
        Assert.Throws<MigrationSelectionException>(() => _generator.Generate(source, target, new(keys)));
        keys.AddRange(items.Indexes.Select(i => new MigrationObjectKey(SchemaObjectKind.Index, items.Key, i.Name)));
        keys.AddRange(items.Constraints.Select(c => new MigrationObjectKey(SchemaObjectKind.Constraint, items.Key, c.Name)));
        var error = Assert.Throws<MigrationSelectionException>(() => _generator.Generate(source, target, new(keys)));
        Assert.Contains(fk.Name, error.Message);
        keys.Add(new(SchemaObjectKind.Constraint, "sdd_select.child", fk.Name));
        var sql = _generator.Generate(source, target, new(keys));
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("child_pkey", sql);
        await ExecuteAsync(Right, sql);
        Assert.All(SchemaComparer.Compare(source, await LoadAsync(Right)), d => Assert.Equal(DifferenceKind.Equal, d.Kind));
    }

    [Fact]
    public async Task EqualForeignKeyMustBeIncludedIfSelectedTypeChangesRequireRebuildingIt()
    {
        if (Left is null || Right is null) return;
        await ResetAsync(Left, """
            CREATE TABLE sdd_select.parent (id bigint PRIMARY KEY);
            CREATE TABLE sdd_select.child (parent_id bigint REFERENCES sdd_select.parent(id));
            """);
        await ResetAsync(Right, """
            CREATE TABLE sdd_select.parent (id integer PRIMARY KEY);
            CREATE TABLE sdd_select.child (parent_id integer REFERENCES sdd_select.parent(id));
            """);
        var source = await LoadAsync(Left); var target = await LoadAsync(Right);
        var keys = new List<MigrationObjectKey> { new(SchemaObjectKind.Column, "sdd_select.parent", "id"), new(SchemaObjectKind.Column, "sdd_select.child", "parent_id") };
        var error = Assert.Throws<MigrationSelectionException>(() => _generator.Generate(source, target, new(keys)));
        Assert.Contains("child_parent_id_fkey", error.Message);
        keys.Add(new(SchemaObjectKind.Constraint, "sdd_select.child", "child_parent_id_fkey"));
        var sql = _generator.Generate(source, target, new(keys));
        Assert.Contains("DROP CONSTRAINT \"child_parent_id_fkey\"", sql);
        await ExecuteAsync(Right, sql);
        Assert.All(SchemaComparer.Compare(source, await LoadAsync(Right)), d => Assert.Equal(DifferenceKind.Equal, d.Kind));
    }

    [Fact]
    public async Task GeneratedColumnReplacementRebuildsChosenIndexAndConstraint()
    {
        if (Left is null || Right is null) return;
        await ResetAsync(Left, """
            CREATE TABLE sdd_select.items (base integer, calculated integer GENERATED ALWAYS AS (base + 2) STORED CHECK (calculated > 0));
            CREATE INDEX calculated_idx ON sdd_select.items(calculated);
            """);
        await ResetAsync(Right, """
            CREATE TABLE sdd_select.items (base integer, calculated integer GENERATED ALWAYS AS (base + 1) STORED CHECK (calculated > 0));
            CREATE INDEX calculated_idx ON sdd_select.items(calculated);
            """);
        var source = await LoadAsync(Left); var target = await LoadAsync(Right);
        var keys = new List<MigrationObjectKey> { new(SchemaObjectKind.Column, "sdd_select.items", "calculated") };
        Assert.Throws<MigrationSelectionException>(() => _generator.Generate(source, target, new(keys)));
        keys.Add(new(SchemaObjectKind.Constraint, "sdd_select.items", "items_calculated_check"));
        keys.Add(new(SchemaObjectKind.Index, "sdd_select.items", "calculated_idx"));
        await ExecuteAsync(Right, _generator.Generate(source, target, new(keys)));
        Assert.All(SchemaComparer.Compare(source, await LoadAsync(Right)), d => Assert.Equal(DifferenceKind.Equal, d.Kind));
    }

    private async Task<SchemaSnapshot> LoadAsync(string connection)
    {
        var all = await _reader.LoadAsync(connection);
        return new(all.Tables.Where(t => t.Schema == "sdd_select").ToArray());
    }
    private static Task ResetAsync(string connection, string sql) => ExecuteAsync(connection, "DROP SCHEMA IF EXISTS sdd_select CASCADE; CREATE SCHEMA sdd_select; " + sql);
    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connection);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }
}
