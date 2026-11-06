using System.Collections.ObjectModel;
using AwesomeAssertions;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// GridFillerRows in a lines table set up as the app's are (UsageFormPanel.xaml): padding must never move the grid's
/// current cell, or leave it somewhere it can't be. As rows of the grid, adding the padding would
/// move it, and moving it back to a cell that no longer exists throws "InValid RowColumnIndex" from a dispatcher
/// callback, which closes the app. Drawn, the padding can't touch the current
/// cell, and these pin that it doesn't. (The add row's current cell when its lines go is GridAddNewRowCurrentCellTests'.)
/// </summary>
[NotInParallel(OffscreenGridCollection.Name)]
public class GridFillerRowsCurrentCellTests
{
    public sealed class Line
    {
        public string Name { get; set; } = string.Empty;

        public decimal Grams { get; set; }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Replacing_the_lines_after_one_was_selected_does_not_crash(bool hasAddRow)
    {
        // Found by fuzzing: selecting a line puts the current cell on its row, and clearing the lines leaves the grid
        // holding that row. The filler rows then added to the empty table made the grid drop it, and moving it back
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

    [Test]
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

    [Test]
    public void More_padding_under_the_last_line_leaves_the_current_cell_on_it()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var lastLine = new RowColumnIndex(grid.ResolveToRowIndex(((ObservableCollection<Line>)grid.ItemsSource).Count - 1), grid.GetFirstColumnIndex());
            grid.MoveCurrentCell(lastLine);
            OffscreenGrid.Pump();

            // Taller: more padding. Added as rows, they moved a current cell on the last line onto the add row.
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
