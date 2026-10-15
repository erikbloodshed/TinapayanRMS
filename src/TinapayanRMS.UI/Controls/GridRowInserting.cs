using System.Collections;
using System.Windows;
using System.Windows.Input;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Lets a blank line be put in above the current one of an editable SfDataGrid, where the add row only ever adds at the
/// bottom (2026-10-15, at the user's request, for the ingredient lines of Sub-Assemblies and of a product's sections).
/// Set <c>controls:GridRowInserting.IsEnabled="True"</c> on a grid whose ItemsSource is an ObservableCollection of lines
/// and whose <see cref="GridAddNewRow"/>.ItemFactory makes them; the new line is the factory's, as the add row's is.
/// <para>
/// <see cref="InsertLine"/> does it: the Insert key on the grid while no cell is being typed into, a button whose
/// CommandTarget is the grid, and the Vim key <c>O</c> (<see cref="InsertAbove"/>). The line goes in above the line the
/// current cell is on, or at the bottom when it's on the add row or nowhere, as one undo step when the grid has a
/// <see cref="GridUndoSteps"/> recorder. Then its first column is opened for typing, as the add row's is. A line left
/// blank stays a blank line, which Save leaves out (IngredientLineRow.CompleteLines) and Undo or Remove Line takes away.
/// </para>
/// </summary>
public static class GridRowInserting
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GridRowInserting), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Puts a blank line in above the current one. Insert on the grid; a button reaches it through its CommandTarget.</summary>
    public static RoutedUICommand InsertLine { get; } = new(
        "Insert Line", nameof(InsertLine), typeof(GridRowInserting), [new KeyGesture(Key.Insert)]);

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static readonly CommandBinding Binding = new(InsertLine, (sender, _) => InsertAbove((SfDataGrid)sender), OnCanInsert);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid)
        {
            return;
        }

        grid.CommandBindings.Remove(Binding);
        if ((bool)e.NewValue)
        {
            grid.CommandBindings.Add(Binding);
        }
    }

    /// <summary>
    /// Not while a cell is being typed into, where Insert is the text box's own key: the new line's open cell included, so
    /// Insert held down puts in one line. A button's click has ended any edit by the time it lands (GridEditCommit), so
    /// the button is never refused for it.
    /// </summary>
    private static void OnCanInsert(object sender, CanExecuteRoutedEventArgs e)
    {
        var grid = (SfDataGrid)sender;
        e.CanExecute = CanInsert(grid) && grid.SelectionController?.CurrentCellManager.CurrentCell is not { IsEditing: true };
    }

    /// <summary>Whether a line can go in: the grid's lines can be changed right now, and it knows how to make one.</summary>
    public static bool CanInsert(SfDataGrid grid) =>
        GetIsEnabled(grid)
        && grid.AllowEditing
        && grid.IsVisible
        && grid.ItemsSource is IList { IsReadOnly: false, IsFixedSize: false }
        && GridAddNewRow.GetItemFactory(grid) is not null;

    /// <summary>
    /// Puts a blank line in above the line the current cell is on, or at the bottom from the add row, then opens its first
    /// column for typing. An edit still open is committed first, a half-typed add row with it, as leaving the grid would.
    /// </summary>
    public static void InsertAbove(SfDataGrid grid)
    {
        if (!CanInsert(grid) || grid.ItemsSource is not IList lines)
        {
            return;
        }

        // Read before the commit, which moves the current cell when it adds the add row's line.
        var currentRow = grid.SelectionController?.CurrentCellManager.CurrentRowColumnIndex.RowIndex ?? -1;
        var above = grid.IsAddNewIndex(currentRow)
            ? null
            : grid.ResolveToRecordIndex(currentRow) is var record and >= 0 && record < grid.View.Records.Count
                ? grid.View.Records[record].Data
                : grid.SelectedItem;

        GridEditCommit.Commit(grid);

        // The line goes in at its place in the lines' own order, which is the one a sorted view would hide.
        grid.SortColumnDescriptions.Clear();
        var index = above is null ? -1 : lines.IndexOf(above);
        if (index < 0)
        {
            index = lines.Count;
        }

        var line = GridAddNewRow.GetItemFactory(grid)!();
        var recorder = GridUndoSteps.GetRecorder(grid);
        recorder?.BeginUndoStep();
        try
        {
            lines.Insert(index, line);
        }
        finally
        {
            recorder?.EndUndoStep();
        }

        grid.SelectedItem = line;
        grid.Focus();

        // The grid lays the change out now rather than when it gets round to it, so the new line has its row and its
        // place before the cursor goes there. Scrolled to before that, from a table scrolled down, the view jumped back to
        // the top and left the new line half out of sight at the bottom (2026-10-15). And with its cell opened only later,
        // a key pressed in between (a fast typist, Insert held down) landed on the line it went in above. Open here, the
        // cell takes every key after the Insert, so holding Insert down puts in the one line.
        grid.UpdateLayout();
        if (grid.ResolveToRowIndex(line) is not (var row and >= 0))
        {
            return;
        }

        var cell = new RowColumnIndex(row, grid.GetFirstColumnIndex());
        grid.MoveCurrentCell(cell);
        grid.ScrollInView(cell);
        grid.UpdateLayout();
        grid.SelectionController?.CurrentCellManager.BeginEdit();
    }
}
