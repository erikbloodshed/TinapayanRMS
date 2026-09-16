using System.Windows;
using System.Windows.Threading;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Cells;
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
/// <item><c>controls:GridAddNewRow.HideCheckBoxes="True"</c> leaves a <c>GridCheckBoxColumn</c>'s cell blank in the add row, so
/// the row stays the plain empty row the other grids show, instead of offering a tick for a line that doesn't exist yet
/// (<see cref="AddRowHiddenCheckBoxRenderer"/>). The tick appears once the line is committed.</item>
/// <item><c>controls:GridAddNewRow.ShowWhen="{Binding CanAddUnit}"</c> shows the add row at the bottom while the bound flag is
/// true and hides it while it's false, for a table with a limit on its lines. Unlike binding <c>AddNewRowPosition</c>, it
/// changes the position once the grid has finished what it's doing: a flag that turns false because the add row was just
/// committed changes while the grid is still adding that line to the collection, and hiding the add row then makes the
/// grid commit it a second time, which throws "Cannot change ObservableCollection during a CollectionChanged event".</item>
/// </list>
/// </summary>
public static class GridAddNewRow
{
    public static readonly DependencyProperty ItemFactoryProperty = DependencyProperty.RegisterAttached(
        "ItemFactory", typeof(Func<object>), typeof(GridAddNewRow), new PropertyMetadata(null, OnItemFactoryChanged));

    public static readonly DependencyProperty DropWhenUntouchedProperty = DependencyProperty.RegisterAttached(
        "DropWhenUntouched", typeof(bool), typeof(GridAddNewRow), new PropertyMetadata(false, OnDropWhenUntouchedChanged));

    public static readonly DependencyProperty HideCheckBoxesProperty = DependencyProperty.RegisterAttached(
        "HideCheckBoxes", typeof(bool), typeof(GridAddNewRow), new PropertyMetadata(false, OnHideCheckBoxesChanged));

    public static readonly DependencyProperty ShowWhenProperty = DependencyProperty.RegisterAttached(
        "ShowWhen", typeof(bool), typeof(GridAddNewRow), new PropertyMetadata(false, OnShowWhenChanged));

    public static Func<object>? GetItemFactory(DependencyObject element) => (Func<object>?)element.GetValue(ItemFactoryProperty);

    public static void SetItemFactory(DependencyObject element, Func<object>? value) => element.SetValue(ItemFactoryProperty, value);

    public static bool GetDropWhenUntouched(DependencyObject element) => (bool)element.GetValue(DropWhenUntouchedProperty);

    public static void SetDropWhenUntouched(DependencyObject element, bool value) => element.SetValue(DropWhenUntouchedProperty, value);

    public static bool GetHideCheckBoxes(DependencyObject element) => (bool)element.GetValue(HideCheckBoxesProperty);

    public static void SetHideCheckBoxes(DependencyObject element, bool value) => element.SetValue(HideCheckBoxesProperty, value);

    public static bool GetShowWhen(DependencyObject element) => (bool)element.GetValue(ShowWhenProperty);

    public static void SetShowWhen(DependencyObject element, bool value) => element.SetValue(ShowWhenProperty, value);

    private static void OnShowWhenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid)
        {
            return;
        }

        // Before the grid is shown nothing is being edited, so the position can be set straight away; after, it waits
        // for whatever the grid is in the middle of, and reads the flag then, so only its latest value counts.
        if (!grid.IsLoaded)
        {
            ApplyShowWhen(grid);
            return;
        }

        grid.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => ApplyShowWhen(grid));
    }

    private static void ApplyShowWhen(SfDataGrid grid) =>
        grid.AddNewRowPosition = GetShowWhen(grid) ? AddNewRowPosition.Bottom : AddNewRowPosition.None;

    private static void OnHideCheckBoxesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid || !(bool)e.NewValue)
        {
            return;
        }

        // The documented way to change how a cell type draws: swap the grid's renderer for it.
        grid.CellRenderers.Remove(AddRowHiddenCheckBoxRenderer.CellType);
        grid.CellRenderers.Add(AddRowHiddenCheckBoxRenderer.CellType, new AddRowHiddenCheckBoxRenderer());
    }

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

/// <summary>
/// A <c>GridCheckBoxColumn</c>'s renderer that hides the tick in the add row. The same checkbox serves every row as rows
/// scroll and recycle, and the grid re-initializes it each time (<c>OnUpdateEditBinding</c> calls
/// <see cref="OnInitializeEditElement"/>), so it's shown again whenever the row it lands on is a real one.
/// </summary>
public sealed class AddRowHiddenCheckBoxRenderer : GridCellCheckBoxRenderer
{
    public const string CellType = "CheckBox";

    public override void OnInitializeEditElement(DataColumnBase dataColumn, System.Windows.Controls.CheckBox uiElement, object dataContext)
    {
        base.OnInitializeEditElement(dataColumn, uiElement, dataContext);
        if (dataColumn is not null && uiElement is not null)
        {
            uiElement.Visibility = DataGrid.IsAddNewIndex(dataColumn.RowIndex) ? Visibility.Hidden : Visibility.Visible;
        }
    }
}
