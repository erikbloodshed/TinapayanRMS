using System.Collections.ObjectModel;
using AwesomeAssertions;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using TinapayanRMS.Tests.Fixtures;
using TinapayanRMS.UI.Controls;

namespace TinapayanRMS.Tests.Controls;

/// <summary>
/// GridEditCommit's commit of a half-typed add row when the cursor leaves a lines table set up as the app's are
/// (UsageFormPanel.xaml). The grid left its current cell on the add row, a fresh one once the line was added, so the
/// outline jumped to the row under the line being typed (Products' sub-assembly lines, 2026-09-27).
/// </summary>
public class GridEditCommitTests
{
    public sealed class Line
    {
        public string Name { get; set; } = string.Empty;

        public decimal Grams { get; set; }
    }

    [Fact]
    public void Committing_the_add_row_on_leaving_keeps_the_current_cell_on_the_added_line()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            var grams = grid.GetFirstColumnIndex() + 1;
            var added = TypeIntoAddRow(grid, grams);

            GridEditCommit.Commit(grid, keepCurrentCell: true);
            OffscreenGrid.Pump();

            lines.Should().HaveCount(3).And.Contain(added);
            grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex.Should().Be(new RowColumnIndex(grid.ResolveToRowIndex(added), grams));
        });
    }

    [Fact]
    public void Committing_the_add_row_for_an_undo_leaves_the_current_cell_on_the_add_row()
    {
        // Ctrl+Z commits first and then takes the line straight back out, so the cell stays where the next line is typed.
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var grams = grid.GetFirstColumnIndex() + 1;
            TypeIntoAddRow(grid, grams);

            GridEditCommit.Commit(grid);
            OffscreenGrid.Pump();

            grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex.Should().Be(new RowColumnIndex(grid.GetAddNewRowIndex(), grams));
        });
    }

    /// <summary>Starts the add row in the given column, as a tap on it does, and gives its line a name.</summary>
    private static Line TypeIntoAddRow(SfDataGrid grid, int column)
    {
        grid.MoveCurrentCell(new RowColumnIndex(grid.GetAddNewRowIndex(), column));
        grid.SelectionController.CurrentCellManager.BeginEdit();
        OffscreenGrid.Pump();

        grid.View.IsAddingNew.Should().BeTrue();
        var line = (Line)grid.View.CurrentAddItem;
        line.Name = "Sugar";
        return line;
    }

    /// <summary>A 200 px lines table with two lines and an add row, with the app's add-row and commit behaviours.</summary>
    private static SfDataGrid LinesGrid()
    {
        var grid = new SfDataGrid
        {
            ItemsSource = new ObservableCollection<Line>
            {
                new() { Name = "Flour", Grams = 1000m },
                new() { Name = "Water", Grams = 650m },
            },
            AutoGenerateColumns = true,
            AllowEditing = true,
            AllowDeleting = true,
            EditTrigger = EditTrigger.OnTap,
            AddNewRowPosition = AddNewRowPosition.Bottom,
            SelectionMode = GridSelectionMode.Single,
            Height = 200,
        };

        GridAddNewRow.SetDropWhenUntouched(grid, true);
        GridEditCommit.SetOnFocusLeave(grid, true);
        GridFillerRows.SetIsEnabled(grid, true);
        return grid;
    }
}
