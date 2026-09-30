using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using Syncfusion.UI.Xaml.TreeGrid;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Keyboard focus in every SfDataGrid sits on a cell, never on the grid as a whole (2026-09-27, at the user's request:
/// "Keyboard focus must be on the grid instead of the outline. It's absurd and nonstandard"):
/// <list type="bullet">
/// <item>No dotted focus rectangle round the whole grid. WPF drew it whenever the grid itself took focus by keyboard:
/// tabbed into, or handed focus back as a cell's edit ended. The current cell's outline already shows where the
/// keyboard is. A local value set as the grid loads, as App sets its pickers' defaults, so no Style replaces the
/// theme's; a FocusVisualStyle set in XAML still wins.</item>
/// <item>Tabbing into a grid puts its current cell on a row (<see cref="PlaceCurrentCell"/>), so the keys act on a cell
/// straight away, as in a spreadsheet. A grid that already has one keeps it.</item>
/// </list>
/// Registered once for the whole app, from App's static constructor, so a grid added later gets it too.
/// </summary>
public static class GridKeyboardFocus
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        EventManager.RegisterClassHandler(typeof(SfDataGrid), FrameworkElement.LoadedEvent, new RoutedEventHandler(HideFocusRectangle));

        // The read-only tree grids (Products' Scaling tab) lose the rectangle too; they have no current cell to place.
        EventManager.RegisterClassHandler(typeof(SfTreeGrid), FrameworkElement.LoadedEvent, new RoutedEventHandler(HideFocusRectangle));
        EventManager.RegisterClassHandler(
            typeof(SfDataGrid), UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(EnterOnCell));
    }

    private static void HideFocusRectangle(object sender, RoutedEventArgs e)
    {
        var grid = (FrameworkElement)sender;
        if (grid.ReadLocalValue(FrameworkElement.FocusVisualStyleProperty) == DependencyProperty.UnsetValue)
        {
            grid.FocusVisualStyle = null;
        }
    }

    private static void EnterOnCell(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Only the grid itself taking focus, and only by keyboard: a click places the current cell where it lands, and
        // a cell's own editor taking focus is already on one.
        if (sender is not SfDataGrid grid || !ReferenceEquals(e.NewFocus, grid) || InputManager.Current.MostRecentInputDevice is not KeyboardDevice)
        {
            return;
        }

        // Once the focus change is through: moving the current cell can move focus itself.
        grid.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (grid.IsKeyboardFocused)
            {
                PlaceCurrentCell(grid);
            }
        });
    }

    /// <summary>
    /// Gives a grid without a current cell one: on the selected row, or else the first line, or else, in an empty
    /// table, the add row; in the first column. A list that selects whole rows (NavigationMode Row) is only given one
    /// on its selected row: picking a row there opens it in the screen's form, which tabbing past mustn't do.
    /// </summary>
    public static void PlaceCurrentCell(SfDataGrid grid)
    {
        if (grid.SelectionController?.CurrentCellManager is not { } cells || cells.HasCurrentCell || grid.Columns.Count == 0)
        {
            return;
        }

        var selectedRow = grid.SelectedItem is { } selected ? grid.ResolveToRowIndex(selected) : -1;
        var row = selectedRow >= 0 || grid.NavigationMode == NavigationMode.Row
            ? selectedRow
            : grid.GetFirstDataRowIndex() is var first and >= 0
                ? first
                : grid.AddNewRowPosition != AddNewRowPosition.None ? grid.GetAddNewRowIndex() : -1;
        if (row < 0)
        {
            return;
        }

        grid.MoveCurrentCell(new RowColumnIndex(row, grid.GetFirstColumnIndex()), needToClearSelection: selectedRow < 0);
    }
}
