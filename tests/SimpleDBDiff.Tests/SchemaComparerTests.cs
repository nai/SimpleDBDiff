using SimpleDBDiff.Core;
using SimpleDBDiff.Postgres;

namespace SimpleDBDiff.Tests;

public sealed class SchemaComparerTests
{
    private static readonly TableSchema Source = new("public", "orders",
        [new("id", "integer", false, null), new("note", "text", true, "'new'::text")],
        [new("orders_pkey", 'p', "PRIMARY KEY (id)")],
        [new("orders_note_idx", "CREATE INDEX orders_note_idx ON public.orders USING btree (note)")]);

    [Fact]
    public void EqualSchemasHaveNoChangesOrSql()
    {
        var left = new SchemaSnapshot([Source]);
        var right = new SchemaSnapshot([Source with { Columns = [new("id", "integer", false, null), new("note", "text", true, "  'new'::text  ")] }]);
        Assert.All(SchemaComparer.Compare(left, right), d => Assert.Equal(DifferenceKind.Equal, d.Kind));
        Assert.Contains("No migration", new PostgresMigrationGenerator().Generate(left, right));
    }

    [Fact]
    public void ChangedAndOneSidedObjectsAreReportedIndependently()
    {
        var target = Source with
        {
            Columns = [new("id", "bigint", false, null), new("target_only", "text", true, null)],
            Constraints = [], Indexes = []
        };
        var results = SchemaComparer.Compare(new([Source]), new([target, new("public", "extra", [], [], [])]));
        Assert.Contains(results, d => d.ObjectType == "Column public.orders" && d.ObjectName == "id" && d.Kind == DifferenceKind.Different);
        Assert.Contains(results, d => d.ObjectName == "note" && d.Kind == DifferenceKind.LeftOnly);
        Assert.Contains(results, d => d.ObjectName == "target_only" && d.Kind == DifferenceKind.RightOnly);
        Assert.Contains(results, d => d.ObjectName == "public.extra" && d.Kind == DifferenceKind.RightOnly);
        Assert.Contains(results, d => d.ObjectName == "orders_pkey" && d.Kind == DifferenceKind.LeftOnly);
        Assert.Contains(results, d => d.ObjectName == "orders_note_idx" && d.Kind == DifferenceKind.LeftOnly);
    }

    [Fact]
    public void SwappingReversesMigrationDirection()
    {
        var left = new SchemaSnapshot([Source]);
        var right = new SchemaSnapshot([]);
        var generator = new PostgresMigrationGenerator();
        Assert.Contains("CREATE TABLE \"public\".\"orders\"", generator.Generate(left, right));
        Assert.Contains("DROP TABLE \"public\".\"orders\"", generator.Generate(right, left));
        Assert.Equal(DifferenceKind.RightOnly, SchemaComparer.Compare(right, left).Single().Kind);
    }

    [Fact]
    public void NormalizationPreservesWhitespaceInsideSqlLiterals()
    {
        Assert.Equal("DEFAULT 'two  spaces'", SchemaComparer.Normalize("  DEFAULT   'two  spaces'  "));
        var changed = Source with { Columns = [new("id", "integer", false, null), new("note", "text", true, "'new  value'::text")] };
        Assert.Contains(SchemaComparer.Compare(new([Source]), new([changed])), d => d.ObjectName == "note" && d.Kind == DifferenceKind.Different);
    }
}
