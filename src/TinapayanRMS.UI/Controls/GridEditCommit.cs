using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Commits an editable SfDataGrid's open edit when keyboard focus leaves it. A cell being typed into, or the
/// add row being filled in, only reaches its item when the edit ends, and the grid doesn't end it on its own
/// when the user clicks away, so a line typed just before Apply or Save would be missed. Set
/// <c>controls:GridEditCommit.OnFocusLeave="True"</c> on a grid that lines are typed straight into.
/// </summary>
public static class GridEditCommit
{
    public static readonly DependencyProperty OnFocusLeaveProperty = DependencyProperty.RegisterAttached(
        "OnFocusLeave", typeof(bool), typeof(GridEditCommit), new PropertyMetadata(false, OnOnFocusLeaveChanged));

    public static bool GetOnFocusLeave(DependencyObject element) => (bool)element.GetValue(OnFocusLeaveProperty);

    public static void SetOnFocusLeave(DependencyObject element, bool value) => element.SetValue(OnFocusLeaveProperty, value);

    private static void OnOnFocusLeaveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid)
        {
            return;
        }

        grid.IsKeyboardFocusWithinChanged -= CommitWhenFocusLeaves;
        if ((bool)e.NewValue)
        {
            grid.IsKeyboardFocusWithinChanged += CommitWhenFocusLeaves;
        }
    }

    private static void CommitWhenFocusLeaves(object sender, DependencyPropertyChangedEventArgs e)
    {
        // A cell's dropdown opens in a popup, outside the grid's visual tree, but focus there is still in the grid.
        var focused = Keyboard.FocusedElement;
        if ((bool)e.NewValue || sender is not SfDataGrid grid || VisualTreeWalk.IsWithin(grid, focused as DependencyObject))
        {
            return;
        }

        // Focus that went nowhere is usually the window losing it to another app, and the window hands it back to the
        // cell on return, so the edit is left open to carry on with: committed, a half-typed add row became a line and
        // the current cell jumped to the new add row under it. Whether the window was left is only settled once the
        // activation messages are through, so it's asked then; the grid may have its focus back by then too.
        if (focused is null)
        {
            grid.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (!grid.IsKeyboardFocusWithin && Window.GetWindow(grid) is { IsActive: true })
                {
                    Commit(grid, keepCurrentCell: true);
                }
            });
            return;
        }

        Commit(grid, keepCurrentCell: true);

        // Ending the add row takes focus with it, so the box that was just clicked would lose the click.
        if (!ReferenceEquals(Keyboard.FocusedElement, focused))
        {
            Keyboard.Focus(focused);
        }
    }

    /// <summary>
    /// Ends the current cell's edit, then adds the add row's item if something was typed into it. One left as
    /// it started (every column still at a new item's value) is dropped instead of becoming an empty line.
    /// With <paramref name="keepCurrentCell"/>, a line added this way keeps the current cell, in the column it was in:
    /// the grid leaves it on the add row, a fresh one once the line is added, so it showed a row below the line typed.
    /// </summary>
    public static void Commit(SfDataGrid grid, bool keepCurrentCell = false)
    {
        var column = grid.SelectionController?.CurrentCellManager.CurrentRowColumnIndex.ColumnIndex ?? -1;
        grid.SelectionController?.CurrentCellManager.EndEdit(true);
        if (grid.View is not { IsAddingNew: true } view)
        {
            return;
        }

        var addNewRow = grid.GetAddNewRowController();
        var item = view.CurrentAddItem;
        if (GridAddNewRow.IsUntouched(grid, item))
        {
            addNewRow.CancelAddNew();
            return;
        }

        addNewRow.CommitAddNew(true);
        if (keepCurrentCell && column >= 0 && grid.ResolveToRowIndex(item) is var row and >= 0)
        {
            grid.MoveCurrentCell(new RowColumnIndex(row, column));
        }
    }
}
