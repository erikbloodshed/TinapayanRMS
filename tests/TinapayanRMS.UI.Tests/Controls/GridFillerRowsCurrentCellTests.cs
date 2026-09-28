using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using AwesomeAssertions;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// GridFillerRows in a lines table set up as the app's are (UsageFormPanel.xaml): adding filler rows must never move
/// the grid's current cell somewhere it can't be. Moving it back to a cell that no longer existed threw "InValid
/// RowColumnIndex" from a dispatcher callback, which closed the app (twice on the Products screen, 2026-09-16).
/// </summary>
[Collection(OffscreenGridCollection.Name)]
public class GridFillerRowsCurrentCellTests
{
    public sealed class Line
    {
        public string Name { get; set; } = string.Empty;

        public decimal Grams { get; set; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replacing_the_lines_after_one_was_selected_does_not_crash(bool hasAddRow)
    {
        // Found by fuzzing: selecting a line puts the current cell on its row, and clearing the lines leaves the grid
        // holding that row. The filler rows then added to the empty table make the grid drop it, and moving it back
        // threw. On the Products screen: pick a line, then open another sub-assembly's form.
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            grid.AddNewRowPosition = hasAddRow ? AddNewRowPosition.Bottom : AddNewRowPosition.None;
            grid.AllowEditing = hasAddRow;
            lines.Add(new Line { Name = "Sugar", Grams = 50m });
            lines.Add(new Line { Name = "Salt", Grams = 20m });
            OffscreenGrid.Pump();

            grid.SelectedItem = lines[^1];
            OffscreenGrid.Pump();
            lines.Clear();

            var act = OffscreenGrid.Pump;

            act.Should().NotThrow();
        });
    }

    [Fact]
    public void Removing_the_line_the_current_cell_is_on_does_not_crash()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            grid.SelectedItem = lines[^1];
            OffscreenGrid.Pump();

            lines.RemoveAt(lines.Count - 1);
            lines.RemoveAt(lines.Count - 1);

            var act = OffscreenGrid.Pump;

            act.Should().NotThrow();
        });
    }

    [Fact]
    public void Filler_rows_added_under_the_last_line_leave_the_current_cell_on_it()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var lastLine = new RowColumnIndex(grid.ResolveToRowIndex(((ObservableCollection<Line>)grid.ItemsSource).Count - 1), grid.GetFirstColumnIndex());
            grid.MoveCurrentCell(lastLine);
            OffscreenGrid.Pump();

            // Taller: more filler rows are added, which moves a current cell on the last line onto the add row.
            grid.Height += 150;
            OffscreenGrid.Pump();

            grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex.Should().Be(lastLine);
        });
    }

    [Fact]
    public void Removing_every_line_while_the_add_row_is_current_leaves_one_current_cell_on_the_add_row()
    {
        // Ctrl+Z back to no brands on Raw Materials' Purchase Cost Setup, with the add row's cell current: the grid moved
        // the add row up but kept its current cell at the old row index, a filler row now, and drew an outline on both.
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var column = grid.GetFirstColumnIndex() + 1;
            grid.MoveCurrentCell(new RowColumnIndex(grid.GetAddNewRowIndex(), column));
            OffscreenGrid.Pump();

            ((ObservableCollection<Line>)grid.ItemsSource).Clear();
            OffscreenGrid.Pump();

            var addRow = grid.GetAddNewRowIndex();
            grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex.Should().Be(new RowColumnIndex(addRow, column));
            OutlinedCells(grid).Should().Equal(new RowColumnIndex(addRow, column));

            // The add row's own outline has to go with the next move too, not stay behind.
            grid.MoveCurrentCell(new RowColumnIndex(addRow, column - 1), needToClearSelection: false);
            OffscreenGrid.Pump();

            OutlinedCells(grid).Should().Equal(new RowColumnIndex(addRow, column - 1));
        });
    }

    /// <summary>The cells drawing the current-cell outline.</summary>
    private static List<RowColumnIndex> OutlinedCells(DependencyObject node)
    {
        var cells = new List<RowColumnIndex>();
        if (node is GridCell { CurrentCellBorderVisibility: Visibility.Visible, IsVisible: true, ColumnBase: { } column })
        {
            cells.Add(new RowColumnIndex(column.RowIndex, column.ColumnIndex));
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            cells.AddRange(OutlinedCells(VisualTreeHelper.GetChild(node, i)));
        }

        return cells;
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

        // The untouched-add-row selection controller and commit-on-focus-leave both change how the grid moves its current cell.
        GridAddNewRow.SetDropWhenUntouched(grid, true);
        GridEditCommit.SetOnFocusLeave(grid, true);
        GridFillerRows.SetIsEnabled(grid, true);
        return grid;
    }
}
