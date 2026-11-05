using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Syncfusion.UI.Xaml.Grid;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Marks out one undo step around each change made in an editable SfDataGrid, so a cell typed into, a row added
/// and a row deleted each come back in one press. Set
/// <c>controls:GridUndoSteps.Recorder="{Binding}"</c> on a grid that lines are typed straight into; on a grid whose
/// DataContext is a row rather than the screen, bind the screen through its View
/// (<c>{Binding DataContext, RelativeSource={RelativeSource AncestorType=UserControl}}</c>).
///
/// The step is opened where the change begins and closed where it lands, never the other way round: the screen
/// keeps only the steps that changed something, so a cell opened and left alone, or an add row abandoned with Esc,
/// settles into nothing. That's also why the add row needs no close of its own — the grid raises nothing when an
/// add commits, and the next step opened settles it.
/// </summary>
public static class GridUndoSteps
{
    public static readonly DependencyProperty RecorderProperty = DependencyProperty.RegisterAttached(
        "Recorder", typeof(IUndoRecorder), typeof(GridUndoSteps), new PropertyMetadata(null, OnRecorderChanged));

    public static IUndoRecorder? GetRecorder(DependencyObject element) => (IUndoRecorder?)element.GetValue(RecorderProperty);

    public static void SetRecorder(DependencyObject element, IUndoRecorder? value) => element.SetValue(RecorderProperty, value);

    private static void OnRecorderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid)
        {
            return;
        }

        grid.CurrentCellBeginEdit -= OnCellBeginEdit;
        grid.CurrentCellEndEdit -= OnCellEndEdit;
        grid.CellCheckBoxClick -= OnCheckBoxClick;
        grid.RecordDeleting -= OnRecordDeleting;
        grid.RecordDeleted -= OnRecordDeleted;
        grid.AddNewRowInitiating -= OnAddNewRowInitiating;

        if (e.NewValue is not IUndoRecorder)
        {
            return;
        }

        grid.CurrentCellBeginEdit += OnCellBeginEdit;
        grid.CurrentCellEndEdit += OnCellEndEdit;
        grid.CellCheckBoxClick += OnCheckBoxClick;
        grid.RecordDeleting += OnRecordDeleting;
        grid.RecordDeleted += OnRecordDeleted;
        grid.AddNewRowInitiating += OnAddNewRowInitiating;
    }

    private static IUndoRecorder? RecorderOf(object sender) =>
        sender is DependencyObject element ? GetRecorder(element) : null;

    private static void OnCellBeginEdit(object? sender, CurrentCellBeginEditEventArgs e)
    {
        // An edit refused before it begins (a locked cell, GridCellEditing) never ends either, so it's no step: marked
        // pending, it would keep Save offered with nothing changed.
        if (e.Cancel || (sender is SfDataGrid grid && GridCellEditing.Refuses(grid, e)))
        {
            return;
        }

        if (RecorderOf(sender!) is { } recorder)
        {
            recorder.BeginUndoStep();

            // What's typed into the cell doesn't reach the line until the edit ends, so until then the screen
            // can't tell whether there's anything to save and keeps Save offered: the click that saves is the
            // click that ends the edit, and a Save button disabled under the cursor would swallow it.
            recorder.IsEditPending = true;
        }
    }

    /// <summary>
    /// The row already holds the typed value here: the grid writes it through the cell's binding inside EndEdit,
    /// and only then raises this. Everything the change sets off has run by now too, so the step closes on a form
    /// that has settled.
    /// <para>
    /// An add row with something typed into it is the exception: its item reaches the screen's lines only when the
    /// row is committed, so the edit is still pending until then (<see cref="StayPendingUntilAdded"/>).
    /// </para>
    /// </summary>
    private static void OnCellEndEdit(object? sender, CurrentCellEndEditEventArgs e)
    {
        if (RecorderOf(sender!) is not { } recorder)
        {
            return;
        }

        if (sender is SfDataGrid { View: { IsAddingNew: true } view } grid && !GridAddNewRow.IsUntouched(grid, view.CurrentAddItem))
        {
            StayPendingUntilAdded(grid, recorder, view.CurrentAddItem);
        }
        else
        {
            recorder.IsEditPending = false;
        }

        recorder.EndUndoStep();
    }

    private static readonly DependencyProperty PendingAddItemProperty = DependencyProperty.RegisterAttached(
        "PendingAddItem", typeof(object), typeof(GridUndoSteps), new PropertyMetadata(null));

    /// <summary>
    /// Keeps Save offered while <paramref name="item"/>, typed into on the add row, is still waiting to be added, and
    /// lets it go once the row is committed or dropped (the view's CurrentAddItem moves off it then, read from the
    /// decompiled GridAddNewRowController and CollectionViewAdv, Syncfusion 35.1.41). A click on Save is what ends the
    /// cell's edit: the cell's renderer ends it as the cursor leaves, mid-way through the move to Save. Save measured then
    /// would see nothing new, since the add row's line isn't in the screen's lines yet, and go disabled under the cursor.
    /// Focus would fall back into the grid, so GridEditCommit wouldn't add the line either, and the click would be lost.
    /// </summary>
    private static void StayPendingUntilAdded(SfDataGrid grid, IUndoRecorder recorder, object item)
    {
        if (ReferenceEquals(grid.GetValue(PendingAddItemProperty), item) || grid.View is not INotifyPropertyChanged view)
        {
            return;
        }

        grid.SetValue(PendingAddItemProperty, item);

        void Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IEditableCollectionView.CurrentAddItem) || ReferenceEquals(grid.View?.CurrentAddItem, item))
            {
                return;
            }

            view.PropertyChanged -= Changed;
            grid.ClearValue(PendingAddItemProperty);
            recorder.IsEditPending = false;
        }

        view.PropertyChanged += Changed;
    }

    /// <summary>
    /// The Base column's tick. Its renderer handles the click itself and never opens an edit, so the cell events
    /// above don't fire for it. The value lands after this, so the step is closed on a later dispatcher pass — the
    /// same way GridAddNewRow waits for the grid to finish what it's doing.
    /// </summary>
    private static void OnCheckBoxClick(object? sender, CellCheckBoxClickEventArgs e)
    {
        if (RecorderOf(sender!) is not { } recorder || sender is not SfDataGrid grid)
        {
            return;
        }

        recorder.BeginUndoStep();
        grid.Dispatcher.BeginInvoke(DispatcherPriority.Background, recorder.EndUndoStep);
    }

    private static void OnRecordDeleting(object? sender, RecordDeletingEventArgs e) => RecorderOf(sender!)?.BeginUndoStep();

    private static void OnRecordDeleted(object? sender, RecordDeletedEventArgs e) => RecorderOf(sender!)?.EndUndoStep();

    /// <summary>
    /// A row is about to be filled in. It isn't in the screen's lines yet and won't be until it's committed, which
    /// the grid raises nothing for — the next step opened settles this one, and an add that's abandoned rather than
    /// committed leaves the lines as they were, so it settles into nothing.
    /// </summary>
    private static void OnAddNewRowInitiating(object? sender, AddNewRowInitiatingEventArgs e) => RecorderOf(sender!)?.BeginUndoStep();
}
