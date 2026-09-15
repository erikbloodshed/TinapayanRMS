using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;

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
        if ((bool)e.NewValue || sender is not SfDataGrid grid || IsWithin(grid, focused as DependencyObject))
        {
            return;
        }

        Commit(grid);

        // Ending the add row takes focus with it, so the box that was just clicked would lose the click.
        if (focused is not null && !ReferenceEquals(Keyboard.FocusedElement, focused))
        {
            Keyboard.Focus(focused);
        }
    }

    /// <summary>Whether <paramref name="element"/> is inside <paramref name="grid"/>, following a popup up to the element it belongs to.</summary>
    private static bool IsWithin(SfDataGrid grid, DependencyObject? element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, grid))
            {
                return true;
            }

            if (current is Popup { PlacementTarget: { } target })
            {
                current = target;
                if (ReferenceEquals(current, grid))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Ends the current cell's edit, then adds the add row's item if something was typed into it. One left as
    /// it started (every column still at a new item's value) is dropped instead of becoming an empty line.
    /// </summary>
    public static void Commit(SfDataGrid grid)
    {
        grid.SelectionController?.CurrentCellManager.EndEdit(true);
        if (grid.View is not { IsAddingNew: true } view)
        {
            return;
        }

        var addNewRow = grid.GetAddNewRowController();
        if (IsUntouched(grid, view.CurrentAddItem))
        {
            addNewRow.CancelAddNew();
        }
        else
        {
            addNewRow.CommitAddNew(true);
        }
    }

    private static bool IsUntouched(SfDataGrid grid, object? item)
    {
        if (item?.GetType().GetConstructor(Type.EmptyTypes) is null)
        {
            return false;
        }

        var fresh = Activator.CreateInstance(item.GetType());
        var properties = grid.View.GetPropertyAccessProvider();
        return grid.Columns.All(c => Equals(properties.GetValue(item, c.MappingName), properties.GetValue(fresh, c.MappingName)));
    }
}
