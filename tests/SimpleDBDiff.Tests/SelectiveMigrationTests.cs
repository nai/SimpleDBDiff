using SimpleDBDiff.Core;
using SimpleDBDiff.Postgres;
using SimpleDBDiff.Web.Services;

namespace SimpleDBDiff.Tests;

public sealed class SelectiveMigrationTests
{
    private static readonly TableSchema Source = new("public", "items", [new("id", "integer", false, null), new("changed", "text", true, null), new("new", "text", true, null)], [], []);
    private static readonly TableSchema Target = Source with { Columns = [new("id", "integer", false, null), new("changed", "integer", true, null), new("old", "text", true, null)] };
    private static IReadOnlyList<SchemaDifference> Rows => ComparisonPresentation.Expand(new([Source]), new([Target]), SchemaComparer.Compare(new([Source]), new([Target])));

    [Fact]
    public void TableCountsShowMixedDifferencesWithoutCountingSummaryOrEqualObjects()
    {
        Assert.Equal(new TableDifferenceCounts(1, 1, 1), ComparisonPresentation.Counts(Rows));
    }

    [Fact]
    public void TableToggleSkipsMigrationAndRestoresPreviousObjectChoices()
    {
        var rows = Rows;
        var state = new MigrationSelectionState(rows);
        Assert.True(state.TableState(Source.Key));
        Assert.Equal(3, state.SelectedChangeCount);
        var column = rows.Single(r => r.ObjectName == "changed");
        state.SetObject(column, false);
        Assert.True(state.TableState(Source.Key));
        Assert.False(state.Snapshot().ContainsTable(Source.Key));
        Assert.Equal(2, state.SelectedChangeCount);
        state.SetTable(Source.Key, false);
        Assert.False(state.TableState(Source.Key));
        Assert.Equal(0, state.SelectedObjectCount);
        Assert.False(state.IsSelected(column));
        Assert.True(state.IsSelected(rows.Single(r => r.ObjectName == "new")));
        Assert.Empty(state.Snapshot().Objects);
        var generator = new PostgresMigrationGenerator();
        Assert.DoesNotContain("ALTER TABLE", generator.Generate(new([Source]), new([Target]), state.Snapshot()));
        state.SetTable(Source.Key, true);
        Assert.True(state.TableState(Source.Key));
        Assert.False(state.IsSelected(column));
        Assert.Equal(2, state.SelectedChangeCount);
        var sql = generator.Generate(new([Source]), new([Target]), state.Snapshot());
        Assert.Contains("ADD COLUMN \"new\"", sql);
        Assert.Contains("DROP COLUMN \"old\"", sql);
        Assert.DoesNotContain("\"changed\"", sql);
    }

    [Fact]
    public void BulkTableAndDetailControlsAreIndependent()
    {
        var rows = Rows;
        var state = new MigrationSelectionState(rows);
        var column = rows.Single(r => r.ObjectName == "changed");
        state.SetObject(column, false);
        state.SetAll(false);
        Assert.True(state.IsSelected(rows.Single(r => r.ObjectName == "new")));
        Assert.False(state.IsSelected(column));
        Assert.Empty(state.Snapshot().Objects);
        state.SetAll(true);
        Assert.False(state.IsSelected(column));
        Assert.Equal(2, state.SelectedChangeCount);
        state.SetDetails(Source.Key, false);
        Assert.True(state.TableState(Source.Key));
        Assert.Equal(0, state.SelectedObjectCount);
        state.SetTable(Source.Key, false);
        state.SetDetails(Source.Key, true);
        Assert.False(state.TableState(Source.Key));
        Assert.Empty(state.Snapshot().Objects);
        state.SetTable(Source.Key, true);
        Assert.All(rows, row => Assert.True(state.IsSelected(row)));
        Assert.True(state.Snapshot().ContainsTable(Source.Key));
        Assert.Equal(3, state.SelectedChangeCount);
    }

    [Fact]
    public void SelectionSnapshotDoesNotChangeWhenUserChangesCheckboxesLater()
    {
        var state = new MigrationSelectionState(Rows);
        var snapshot = state.Snapshot();
        state.SetAll(false);
        Assert.True(snapshot.ContainsTable(Source.Key));
        Assert.False(state.Snapshot().ContainsTable(Source.Key));
    }

    [Fact]
    public void OnlyChosenColumnIsMigratedAndExcludedObjectsRemainOnTarget()
    {
        var selection = new MigrationSelection([new(SchemaObjectKind.Column, Source.Key, "new")]);
        var desired = MigrationPlanner.Project(new([Source]), new([Target]), selection);
        Assert.Equal("integer", desired.Tables.Single().Columns.Single(c => c.Name == "changed").DataType);
        Assert.Contains(desired.Tables.Single().Columns, c => c.Name == "old");
        var sql = new PostgresMigrationGenerator().Generate(new([Source]), new([Target]), selection);
        Assert.Contains("ADD COLUMN \"new\"", sql);
        Assert.DoesNotContain("\"changed\"", sql);
        Assert.DoesNotContain("\"old\"", sql);
    }

    [Fact]
    public void PartialRightOnlyTableDropsSelectedMembersWithoutDroppingTable()
    {
        var selection = new MigrationSelection([new(SchemaObjectKind.Column, Target.Key, "old")]);
        var sql = new PostgresMigrationGenerator().Generate(new([]), new([Target]), selection);
        Assert.Contains("DROP COLUMN \"old\"", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("\"id\"", sql);
    }

    [Fact]
    public void PartialLeftOnlyTableCreatesOnlySelectedColumns()
    {
        var selection = new MigrationSelection([new(SchemaObjectKind.Column, Source.Key, "id")]);
        var sql = new PostgresMigrationGenerator().Generate(new([Source]), new([]), selection);
        Assert.Contains("CREATE TABLE \"public\".\"items\" (\"id\" integer NOT NULL)", sql);
        Assert.DoesNotContain("\"new\"", sql);
        Assert.DoesNotContain("\"changed\"", sql);
    }

    [Fact]
    public void ExcludedDependencyPreventsImplicitRemoval()
    {
        var target = Target with { Indexes = [new("old_idx", "CREATE INDEX old_idx ON public.items (old)", ["old"])] };
        var selection = new MigrationSelection([new(SchemaObjectKind.Column, Target.Key, "old")]);
        var error = Assert.Throws<MigrationSelectionException>(() => new PostgresMigrationGenerator().Generate(new([Source]), new([target]), selection));
        Assert.Contains("old_idx", error.Message);
    }

    [Fact]
    public void EmptySelectionGeneratesNoChangesAndNewComparisonCanReverseDirection()
    {
        var generator = new PostgresMigrationGenerator();
        Assert.DoesNotContain("ALTER TABLE", generator.Generate(new([Source]), new([Target]), new([])));
        var forward = new MigrationSelectionState(ComparisonPresentation.Expand(new([Source]), new([]), SchemaComparer.Compare(new([Source]), new([]))));
        Assert.Contains("CREATE TABLE", generator.Generate(new([Source]), new([]), forward.Snapshot()));
        var reverse = new MigrationSelectionState(ComparisonPresentation.Expand(new([]), new([Source]), SchemaComparer.Compare(new([]), new([Source]))));
        Assert.Contains("DROP TABLE", generator.Generate(new([]), new([Source]), reverse.Snapshot()));
    }
}
