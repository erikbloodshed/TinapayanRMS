using System.Windows;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Behaviours for an SfDataGrid's add row:
/// <list type="bullet">
/// <item><c>controls:GridAddNewRow.ItemFactory="{Binding NewLine}"</c> hands the add row the item it edits, made by the
/// screen, instead of one the grid makes with the item type's parameterless constructor (through
/// AddNewRowInitiating's NewObject). Until the add row is committed its item isn't in the grid's collection, so the
/// screen can't reach it; an item the screen made can carry what it needs, such as a line that prices itself as it's
/// typed. Set it to a <c>Func</c> returning the new item.</item>
/// <item><c>controls:GridAddNewRow.DropWhenUntouched="True"</c> drops an add row nothing was typed into when it's left
/// for another row, instead of adding it as an empty line (<see cref="UntouchedAddRowSelectionController"/>). For a
/// grid with row selection, the default <c>SelectionUnit</c>.</item>
/// </list>
/// </summary>
public static class GridAddNewRow
{
    public static readonly DependencyProperty ItemFactoryProperty = DependencyProperty.RegisterAttached(
        "ItemFactory", typeof(Func<object>), typeof(GridAddNewRow), new PropertyMetadata(null, OnItemFactoryChanged));

    public static readonly DependencyProperty DropWhenUntouchedProperty = DependencyProperty.RegisterAttached(
        "DropWhenUntouched", typeof(bool), typeof(GridAddNewRow), new PropertyMetadata(false, OnDropWhenUntouchedChanged));

    public static Func<object>? GetItemFactory(DependencyObject element) => (Func<object>?)element.GetValue(ItemFactoryProperty);

    public static void SetItemFactory(DependencyObject element, Func<object>? value) => element.SetValue(ItemFactoryProperty, value);

    public static bool GetDropWhenUntouched(DependencyObject element) => (bool)element.GetValue(DropWhenUntouchedProperty);

    public static void SetDropWhenUntouched(DependencyObject element, bool value) => element.SetValue(DropWhenUntouchedProperty, value);

    /// <summary>
    /// Whether an add row's item is as it started: every column still at a new item's value. An item without a
    /// parameterless constructor to compare against counts as touched.
    /// </summary>
    internal static bool IsUntouched(SfDataGrid grid, object? item)
    {
        if (item?.GetType().GetConstructor(Type.EmptyTypes) is null)
        {
            return false;
        }

        var fresh = Activator.CreateInstance(item.GetType());
        var properties = grid.View.GetPropertyAccessProvider();
        return grid.Columns.All(c => Equals(properties.GetValue(item, c.MappingName), properties.GetValue(fresh, c.MappingName)));
    }

    private static void OnItemFactoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid)
        {
            return;
        }

        grid.AddNewRowInitiating -= SupplyNewItem;
        if (e.NewValue is not null)
        {
            grid.AddNewRowInitiating += SupplyNewItem;
        }
    }

    private static void SupplyNewItem(object? sender, AddNewRowInitiatingEventArgs e)
    {
        if (sender is SfDataGrid grid && GetItemFactory(grid) is { } factory)
        {
            e.NewObject = factory();
        }
    }

    private static void OnDropWhenUntouchedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid)
        {
            return;
        }

        // The grid makes its selection controller once, when it's constructed (again only if SelectionUnit changes).
        if ((bool)e.NewValue && grid.SelectionController is not UntouchedAddRowSelectionController)
        {
            grid.SelectionController = new UntouchedAddRowSelectionController(grid);
        }
        else if (!(bool)e.NewValue && grid.SelectionController is UntouchedAddRowSelectionController)
        {
            grid.SelectionController = new GridSelectionController(grid);
        }
    }
}

/// <summary>
/// The row selection controller, except that an add row left untouched is dropped rather than added. The grid
/// commits the add row whenever the current cell moves off it: a click on another line (GridCurrentCellManager's
/// HandlePointerOperation), an arrow key or Enter (this controller's key handling). So clicking into the add row and
/// back onto a line added an empty line. Those paths all go through <see cref="CommitAddNew"/>, which now cancels an
/// untouched row the way the grid's own Esc does. A row with anything typed in it is still added.
/// </summary>
internal sealed class UntouchedAddRowSelectionController(SfDataGrid grid) : GridSelectionController(grid)
{
    protected override void CommitAddNew(bool changeState = true)
    {
        if (DataGrid.View is { IsAddingNew: true } view && GridAddNewRow.IsUntouched(DataGrid, view.CurrentAddItem))
        {
            DataGrid.GetAddNewRowController().CancelAddNew();
            return;
        }

        base.CommitAddNew(changeState);
    }
}
