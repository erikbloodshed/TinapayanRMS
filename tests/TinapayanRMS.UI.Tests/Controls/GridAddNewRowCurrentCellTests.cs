using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AwesomeAssertions;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// A current cell on the add row stays on it when the lines above it go (GridAddNewRow's
/// UntouchedAddRowSelectionController.PutCurrentCellBackOnAddRow). SfDataGrid moves the add row up but leaves its current
/// cell index where the add row was: the add row keeps its outline, and the next key moves from the stale index, so Left,
/// Right and Tab each left no cell current. Ctrl+Z back to no brands on Raw Materials' Purchase Cost Setup, or another
/// material with none, with the add row's cell current.
/// </summary>
[NotInParallel(OffscreenGridCollection.Name)]
public class GridAddNewRowCurrentCellTests
{
    public sealed class Line
    {
        public string Name { get; set; } = string.Empty;

        public decimal Grams { get; set; }
    }

    [Test]
    public void Removing_every_line_while_the_add_row_is_current_leaves_one_current_cell_on_the_add_row()
    {
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

    [Test]
    [Arguments(Key.Left, 1, 0, true)]
    [Arguments(Key.Right, 0, 1, true)]
    [Arguments(Key.Tab, 0, 1, true)]
    [Arguments(Key.Left, 1, 0, false)]
    [Arguments(Key.Right, 0, 1, false)]
    [Arguments(Key.Tab, 0, 1, false)]
    public void A_key_after_every_line_is_removed_moves_along_the_add_row(Key key, int from, int to, bool clear)
    {
        // The grid's current cell index is left where the last line was, below the add row; the next key must still
        // move from the add row's cell, as it's drawn. Without the clear, the same keys as a baseline.
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var first = grid.GetFirstColumnIndex();
            grid.MoveCurrentCell(new RowColumnIndex(grid.GetAddNewRowIndex(), first + from));
            grid.Focus();
            OffscreenGrid.Pump();
            if (clear)
            {
                ((ObservableCollection<Line>)grid.ItemsSource).Clear();
            }

            OffscreenGrid.Pump();

            var source = PresentationSource.FromVisual(grid)!;
            var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            grid.RaiseEvent(preview);
            if (!preview.Handled)
            {
                grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
            }

            OffscreenGrid.Pump();

            OutlinedCells(grid).Should().Equal(new RowColumnIndex(grid.GetAddNewRowIndex(), first + to));
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
        return grid;
    }
}
