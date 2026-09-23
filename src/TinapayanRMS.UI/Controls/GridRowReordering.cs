using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Lets an editable SfDataGrid's lines be put in order by dragging them, from the row header or any cell not being
/// typed into, with a line showing where the row will land. Set <c>controls:GridRowReordering.IsEnabled="True"</c> on
/// a grid whose ItemsSource is an ObservableCollection of lines; the order is the collection's, which is what Save
/// writes (every line table stores each line's position).
///
/// The grid's own drop only reorders its view, never the collection behind it, so every drop is handled here: the
/// dragged line is moved in the collection itself, as one undo step when the grid has a
/// <see cref="GridUndoSteps"/> recorder. A grid that's read-only right now (a linked recipe's lines) can't be
/// dragged, and a line can't be dragged out of one table into another, even where both hold the same kind of line.
/// </summary>
public static class GridRowReordering
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GridRowReordering), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid || !(bool)e.NewValue)
        {
            return;
        }

        // Turning dragging on is what creates the grid's drag-drop controller; from then on each press decides it.
        grid.AllowDraggingRows = true;
        grid.AllowDrop = true;
        grid.RowDropIndicatorMode = DropIndicatorMode.Line;
        grid.PreviewMouseDown += ArmOnLinePress;

        var controller = grid.RowDragDropController;
        controller.DragStart += (_, args) => args.Handled = !CanReorder(grid, args.DraggingRecords);
        controller.DragOver += (_, args) => args.Handled = args.IsFromOutSideSource || !grid.AllowEditing;
        controller.Drop += (_, args) =>
        {
            args.Handled = true;
            if (!args.IsFromOutSideSource && CanReorder(grid, args.DraggingRecords))
            {
                MoveLine(grid, args.DraggingRecords[0], args.TargetRecord, args.DropPosition);
            }
        };
    }

    /// <summary>
    /// Arms dragging only for a press on a line of a grid whose lines can be changed. Once a press has armed it, the grid
    /// tries to start a drag on every move until the button comes up, and every try logs Syncfusion telemetry (an HTTP
    /// post each, decompiled from 34.2.7). A read-only grid's drags are refused and the add row has no line to drag, so
    /// either would retry, and post, all the way through the gesture. Runs before the cell's own press handler: this is
    /// the grid, and the press tunnels down to the cell.
    /// </summary>
    private static void ArmOnLinePress(object sender, MouseButtonEventArgs e)
    {
        if (sender is SfDataGrid grid && grid.GetVisualContainer() is { } container)
        {
            var rowIndex = container.PointToCellRowColumnIndex(e.GetPosition(container), false).RowIndex;
            grid.AllowDraggingRows = grid.AllowEditing && grid.ResolveToRecordIndex(rowIndex) >= 0;
        }
    }

    /// <summary>Only one line at a time, only one of the grid's own, and only while its lines can be changed.</summary>
    private static bool CanReorder(SfDataGrid grid, ObservableCollection<object>? dragging) =>
        grid.AllowEditing
        && dragging is [var line]
        && grid.ItemsSource is IList items
        && items.Contains(line);

    /// <summary>
    /// Moves <paramref name="line"/> to just above or below the line it was dropped on. The grid names that one by its
    /// place in the view, which a click on a column header may have sorted; a sorted view is put back in the lines' own
    /// order, which is the one the drop has just changed.
    /// </summary>
    private static void MoveLine(SfDataGrid grid, object line, object targetRecordIndex, DropPosition position)
    {
        if (grid.ItemsSource is not IList items
            || targetRecordIndex is not int recordIndex
            || recordIndex < 0
            || recordIndex >= grid.View.Records.Count
            || position is not (DropPosition.DropAbove or DropPosition.DropBelow))
        {
            return;
        }

        var target = grid.View.Records[recordIndex].Data;
        var from = items.IndexOf(line);
        var targetIndex = items.IndexOf(target);
        if (from < 0 || targetIndex < 0 || ReferenceEquals(line, target))
        {
            return;
        }

        // Where it lands once it's out of its old place: every line after that place moves up one.
        var to = position == DropPosition.DropAbove ? targetIndex : targetIndex + 1;
        if (from < to)
        {
            to--;
        }

        grid.SortColumnDescriptions.Clear();
        if (from == to)
        {
            return;
        }

        var recorder = GridUndoSteps.GetRecorder(grid);
        recorder?.BeginUndoStep();
        try
        {
            // ObservableCollection<T>.Move, whatever the T: one Move notification, so the row stays selected and the
            // screen sees the line moved rather than one line taken out and another added.
            items.GetType().GetMethod(nameof(ObservableCollection<object>.Move), [typeof(int), typeof(int)])!
                .Invoke(items, [from, to]);
        }
        finally
        {
            recorder?.EndUndoStep();
        }

        grid.SelectedItem = line;
    }
}
