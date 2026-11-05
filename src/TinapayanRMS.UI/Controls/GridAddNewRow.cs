using System.Collections.Specialized;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Cells;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using TinapayanRMS.UI.Converters;

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
/// (<see cref="AddRowHiddenCheckBoxRenderer"/>). The tick appears once a line is being typed into the row.</item>
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

    public static Func<object>? GetItemFactory(DependencyObject element) => (Func<object>?)element.GetValue(ItemFactoryProperty);

    public static void SetItemFactory(DependencyObject element, Func<object>? value) => element.SetValue(ItemFactoryProperty, value);

    public static bool GetDropWhenUntouched(DependencyObject element) => (bool)element.GetValue(DropWhenUntouchedProperty);

    public static void SetDropWhenUntouched(DependencyObject element, bool value) => element.SetValue(DropWhenUntouchedProperty, value);

    public static bool GetHideCheckBoxes(DependencyObject element) => (bool)element.GetValue(HideCheckBoxesProperty);

    public static void SetHideCheckBoxes(DependencyObject element, bool value) => element.SetValue(HideCheckBoxesProperty, value);

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
/// <para>
/// An add row still open when the screen changes the grid's lines itself is dropped as well, typed into or not
/// (<see cref="ProcessSourceCollectionChanged"/>), and a current cell on the add row stays on it when the lines above it
/// go (<see cref="PutCurrentCellBackOnAddRow"/>).
/// </para>
/// </summary>
internal sealed class UntouchedAddRowSelectionController(SfDataGrid grid) : GridSelectionController(grid)
{
    /// <summary>
    /// The grid's answer to its lines changing under an open add row is to commit the add row, which adds to the lines
    /// while they're still raising CollectionChanged, and that throws "Cannot change ObservableCollection during a
    /// CollectionChanged event" as soon as the screen listens to them too. Products' Save crashed that way when its
    /// reload refilled the form's lines. There's no committing the row from inside that event, so it's
    /// cancelled the way Esc cancels it. The grid's own commit doesn't pass through here with the row open.
    /// </summary>
    protected override void ProcessSourceCollectionChanged(NotifyCollectionChangedEventArgs e, CollectionChangedReason reason)
    {
        if (reason == CollectionChangedReason.SourceCollectionChanged && DataGrid.View is { IsAddingNew: true })
        {
            CurrentCellManager.EndEdit(canCommit: false);
            DataGrid.GetAddNewRowController().CancelAddNew();
        }

        var wasOnAddRow = DataGrid.IsAddNewIndex(CurrentCellManager.CurrentRowColumnIndex.RowIndex);
        base.ProcessSourceCollectionChanged(e, reason);
        if (wasOnAddRow)
        {
            DataGrid.Dispatcher.BeginInvoke(DispatcherPriority.Background, PutCurrentCellBackOnAddRow);
        }
    }

    /// <summary>
    /// When the last lines go while the add row's cell is current (Ctrl+Z back to no lines, another material with none),
    /// the grid moves the add row up but leaves its current cell index where the add row was, past the last row it can
    /// be on. The add row keeps its outline, but the next key moves from the index, off the rows: Left, Right and Tab
    /// all left no cell current. So the index goes back on the add row's cell. One dispatcher pass after the change: until
    /// the add row is laid out the grid can't find its cell, notes only the index, and the next move leaves it outlined.
    /// </summary>
    private void PutCurrentCellBackOnAddRow()
    {
        // At the bottom the add row is the last row the current cell can be on.
        var current = CurrentCellManager.CurrentRowColumnIndex;
        if (DataGrid.AddNewRowPosition == AddNewRowPosition.Bottom && DataGrid.GetAddNewRowIndex() is var addRow and >= 0 && current.RowIndex > addRow)
        {
            DataGrid.MoveCurrentCell(new RowColumnIndex(addRow, current.ColumnIndex), needToClearSelection: false);
        }
    }

    /// <summary>
    /// Tab from the add row's last cell that takes the keyboard adds the line, as Enter does, and opens the new add row's
    /// first cell for the next line. Left to the grid, Tab only ends the cell's edit there: the row stays an add row with
    /// its values in, not yet a line, so there's no empty row to start the next one in until Enter or a click elsewhere
    /// adds it. An
    /// add row nothing was typed into is left to the grid, so Tab still leaves the table from it.
    /// </summary>
    protected override void ProcessKeyDown(KeyEventArgs args)
    {
        if (args.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) == 0 && TabEndsTypedAddRow())
        {
            args.Handled = true;
            GridEditCommit.Commit(DataGrid);
            if (DataGrid.GetAddNewRowIndex() is var addRow and >= 0)
            {
                DataGrid.MoveCurrentCell(new RowColumnIndex(addRow, DataGrid.GetFirstColumnIndex()));
                CurrentCellManager.BeginEdit();
            }

            return;
        }

        base.ProcessKeyDown(args);
    }

    /// <summary>Whether the current cell is the last one Tab can reach on an add row something was typed into.</summary>
    private bool TabEndsTypedAddRow()
    {
        var current = CurrentCellManager.CurrentRowColumnIndex;
        if (!DataGrid.IsAddNewIndex(current.RowIndex) || DataGrid.View is not { IsAddingNew: true } view || GridAddNewRow.IsUntouched(DataGrid, view.CurrentAddItem))
        {
            return false;
        }

        var column = DataGrid.ResolveToGridVisibleColumnIndex(current.ColumnIndex);
        if (column < 0)
        {
            return false;
        }

        for (var next = column + 1; next < DataGrid.Columns.Count; next++)
        {
            if (DataGrid.Columns[next] is { IsHidden: false, AllowFocus: true })
            {
                return false;
            }
        }

        return true;
    }

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
/// A <c>GridCheckBoxColumn</c>'s renderer that hides the tick in the add row until a line is being typed into it. The same
/// checkbox serves every row as rows scroll and recycle, and the grid re-initializes it each time (<c>OnUpdateEditBinding</c>
/// calls <see cref="OnInitializeEditElement"/>), so it's shown again whenever the row it lands on is a real one.
/// <para>
/// In the add row it follows its DataContext, which is the add row's item from the moment the row is opened until it's
/// committed or dropped, and null otherwise; the grid doesn't initialize the cell again in between. Hidden for the whole
/// add row, the Base box couldn't be ticked while its line is being typed, with the ingredient and grams in and nothing
/// to tick until the line is added. Ticked there, the tick goes into the item and is added with it.
/// </para>
/// </summary>
public sealed class AddRowHiddenCheckBoxRenderer : GridCellCheckBoxRenderer
{
    public const string CellType = "CheckBox";

    private static readonly NotNullToVisibilityConverter ShownWithItem = new();

    public override void OnInitializeEditElement(DataColumnBase dataColumn, System.Windows.Controls.CheckBox uiElement, object dataContext)
    {
        base.OnInitializeEditElement(dataColumn, uiElement, dataContext);
        if (dataColumn is null || uiElement is null)
        {
            return;
        }

        if (DataGrid.IsAddNewIndex(dataColumn.RowIndex))
        {
            // No path: the checkbox's own DataContext.
            uiElement.SetBinding(UIElement.VisibilityProperty, new Binding { Converter = ShownWithItem });
        }
        else
        {
            uiElement.ClearValue(UIElement.VisibilityProperty);
        }
    }
}
