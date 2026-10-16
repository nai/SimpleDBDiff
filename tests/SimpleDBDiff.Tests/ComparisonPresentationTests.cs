using SimpleDBDiff.Core;
using SimpleDBDiff.Web.Services;

namespace SimpleDBDiff.Tests;

public sealed class ComparisonPresentationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OneSidedTableShowsItsColumnConstraintAndIndexOnTheCorrectSide(bool sourceOnly)
    {
        var table = new TableSchema("sales", "order.items", [new("id", "integer", false, null)],
            [new("items_pk", 'p', "PRIMARY KEY (id)")], [new("items_idx", "CREATE INDEX items_idx ON sales.items (id)")]);
        var populated = new SchemaSnapshot([table]);
        var empty = new SchemaSnapshot([]);
        var left = sourceOnly ? populated : empty;
        var right = sourceOnly ? empty : populated;
        var rows = ComparisonPresentation.Expand(left, right, SchemaComparer.Compare(left, right));
        Assert.Equal(4, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(table.Key, ComparisonPresentation.TableKey(row));
            Assert.Equal(sourceOnly ? DifferenceKind.LeftOnly : DifferenceKind.RightOnly, row.Kind);
            Assert.NotNull(sourceOnly ? row.LeftDefinition : row.RightDefinition);
            Assert.Null(sourceOnly ? row.RightDefinition : row.LeftDefinition);
        });
        Assert.Contains(rows, row => row.ObjectName == "id");
        Assert.Contains(rows, row => row.ObjectName == "items_pk");
        Assert.Contains(rows, row => row.ObjectName == "items_idx");
    }
}
