using System.Collections.ObjectModel;
using AwesomeAssertions;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using TinapayanRMS.Tests.Fixtures;
using TinapayanRMS.UI.Controls;

namespace TinapayanRMS.Tests.Controls;

/// <summary>
/// GridFillerRows in a lines table set up as the app's are (UsageFormPanel.xaml): adding filler rows must never move
/// the grid's current cell somewhere it can't be. Moving it back to a cell that no longer existed threw "InValid
/// RowColumnIndex" from a dispatcher callback, which closed the app (twice on the Products screen, 2026-09-16).
/// </summary>
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
