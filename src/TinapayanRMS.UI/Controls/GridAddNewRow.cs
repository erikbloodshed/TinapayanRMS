using System.Windows;
using Syncfusion.UI.Xaml.Grid;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Hands an SfDataGrid's add row the item it edits, made by the screen, instead of one the grid makes with
/// the item type's parameterless constructor (through AddNewRowInitiating's NewObject). Until the add row is
/// committed its item isn't in the grid's collection, so the screen can't reach it; an item the screen made
/// can carry what it needs, such as a line that prices itself as it's typed. Set
/// <c>controls:GridAddNewRow.ItemFactory="{Binding NewLine}"</c> to a <c>Func</c> returning the new item.
/// </summary>
public static class GridAddNewRow
{
    public static readonly DependencyProperty ItemFactoryProperty = DependencyProperty.RegisterAttached(
        "ItemFactory", typeof(Func<object>), typeof(GridAddNewRow), new PropertyMetadata(null, OnItemFactoryChanged));

    public static Func<object>? GetItemFactory(DependencyObject element) => (Func<object>?)element.GetValue(ItemFactoryProperty);

    public static void SetItemFactory(DependencyObject element, Func<object>? value) => element.SetValue(ItemFactoryProperty, value);

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
}
