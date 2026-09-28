using System.Collections.ObjectModel;
using System.Windows;
using AwesomeAssertions;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// GridKeyboardFocus: no dotted focus rectangle round a whole grid, and a grid tabbed into gets a current cell on a row
/// (PlaceCurrentCell), so the keyboard is on a cell, never on the bare grid.
/// </summary>
[Collection(OffscreenGridCollection.Name)]
public class GridKeyboardFocusTests
{
    public sealed class Line
    {
        public string Name { get; set; } = string.Empty;

        public decimal Grams { get; set; }
    }

    [Fact]
    public void A_loaded_grid_draws_no_focus_rectangle()
    {
        GridKeyboardFocus.Register();

        OffscreenGrid.Run(() => LinesGrid(), grid => grid.FocusVisualStyle.Should().BeNull());
    }

    [Fact]
    public void A_lines_table_without_a_current_cell_gets_one_on_its_first_line()
    {
        OffscreenGrid.Run(() => LinesGrid(), grid =>
        {
            grid.SelectionController.CurrentCellManager.HasCurrentCell.Should().BeFalse();

            GridKeyboardFocus.PlaceCurrentCell(grid);
            OffscreenGrid.Pump();

            grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex.Should().Be(new RowColumnIndex(grid.GetFirstDataRowIndex(), grid.GetFirstColumnIndex()));
        });
    }

    [Fact]
    public void An_empty_lines_table_gets_its_current_cell_on_the_add_row()
    {
        OffscreenGrid.Run(() => LinesGrid(lineCount: 0), grid =>
        {
            GridKeyboardFocus.PlaceCurrentCell(grid);
            OffscreenGrid.Pump();

            grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex.Should().Be(new RowColumnIndex(grid.GetAddNewRowIndex(), grid.GetFirstColumnIndex()));
            grid.View.IsAddingNew.Should().BeFalse("landing on the add row doesn't start a line");
        });
    }

    [Fact]
    public void A_current_cell_already_there_is_kept()
    {
        OffscreenGrid.Run(() => LinesGrid(), grid =>
        {
            var second = new RowColumnIndex(grid.GetFirstDataRowIndex() + 1, grid.GetFirstColumnIndex() + 1);
            grid.MoveCurrentCell(second);
            OffscreenGrid.Pump();

            GridKeyboardFocus.PlaceCurrentCell(grid);
            OffscreenGrid.Pump();

            grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex.Should().Be(second);
        });
    }

    [Fact]
    public void A_list_that_opens_rows_is_given_no_row_when_none_is_selected()
    {
        // Selecting a row of the Sub-Assemblies list opens it in the form, which merely tabbing past mustn't do.
        OffscreenGrid.Run(() => LinesGrid(navigationMode: NavigationMode.Row, editable: false), grid =>
        {
            GridKeyboardFocus.PlaceCurrentCell(grid);
            OffscreenGrid.Pump();

            grid.SelectionController.CurrentCellManager.HasCurrentCell.Should().BeFalse();
            grid.SelectedItem.Should().BeNull();
        });
    }

    /// <summary>A 200 px table of <paramref name="lineCount"/> lines, an editable one with an add row unless told otherwise.</summary>
    private static SfDataGrid LinesGrid(int lineCount = 2, NavigationMode navigationMode = NavigationMode.Cell, bool editable = true)
    {
        var grid = new SfDataGrid
        {
            ItemsSource = new ObservableCollection<Line>(Enumerable.Range(1, lineCount).Select(i => new Line { Name = $"Line {i}", Grams = i * 100m })),
            AutoGenerateColumns = true,
            AllowEditing = editable,
            EditTrigger = EditTrigger.OnTap,
            AddNewRowPosition = editable ? AddNewRowPosition.Bottom : AddNewRowPosition.None,
            SelectionMode = GridSelectionMode.Single,
            NavigationMode = navigationMode,
            Height = 200,
        };

        GridAddNewRow.SetDropWhenUntouched(grid, true);
        GridEditCommit.SetOnFocusLeave(grid, true);
        return grid;
    }
}
