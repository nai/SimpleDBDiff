using SimpleDBDiff.Core;
using SimpleDBDiff.Shared.Services;

namespace SimpleDBDiff.Tests;
public sealed class RefreshSelectionTests
{
    private static SchemaSnapshot Schema(params ColumnSchema[] columns) => new([new("public", "items", columns, [], [])]);
    private static IReadOnlyList<SchemaDifference> Rows(SchemaSnapshot l, SchemaSnapshot r) => ComparisonPresentation.Expand(l,r,SchemaComparer.Compare(l,r));
    [Fact]
    public void RefreshPreservesUnchangedChoicesAndClearsChangedAndNewObjects()
    {
        var left = Schema(new ColumnSchema("id", "integer", false, null), new("name", "text", true, null), new("skipped", "text", true, null));
        var right = Schema(new ColumnSchema("id", "integer", false, null));
        var rows = Rows(left, right); var old = new MigrationSelectionState(rows);
        old.SetObject(rows.Single(r=>r.ObjectName == "skipped"), false);
        var checkpoint = old.Capture(left,right);
        var refreshed = Schema(new ColumnSchema("id", "integer", false, null), new("name", "varchar(20)", true, null), new("skipped", "text", true, null), new("new", "boolean", true, null));
        var nextRows = Rows(refreshed,right); var next = new MigrationSelectionState(nextRows);
        next.Restore(checkpoint,refreshed,right);
        Assert.True(next.IsSelected(nextRows.Single(r=>r.ObjectName == "id")));
        Assert.False(next.IsSelected(nextRows.Single(r=>r.ObjectName == "skipped")));
        Assert.False(next.IsSelected(nextRows.Single(r=>r.ObjectName == "name")));
        Assert.False(next.IsSelected(nextRows.Single(r=>r.ObjectName == "new")));
        Assert.Contains(next.NeedsReview,k=>k.Name == "name");
        Assert.Contains(next.NeedsReview,k=>k.Name == "new");
    }
    [Fact]
    public void RefreshRetainsSkippedTableAndHiddenDetailChoices()
    {
        var left = Schema(new ColumnSchema("id", "integer", false, null)); var right = Schema();
        var rows = Rows(left,right); var selection = new MigrationSelectionState(rows);
        selection.SetTable("public.items",false);
        var restored = new MigrationSelectionState(rows); restored.Restore(selection.Capture(left,right),left,right);
        Assert.False(restored.TableState("public.items"));
        Assert.True(restored.IsSelected(rows.Single(r=>r.ObjectName == "id")));
        Assert.Equal(0,restored.SelectedObjectCount);
    }
}
