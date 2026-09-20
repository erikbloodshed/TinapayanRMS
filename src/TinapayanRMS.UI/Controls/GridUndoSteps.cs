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

    private static void OnCellBeginEdit(object? sender, CurrentCellBeginEditEventArgs e) => RecorderOf(sender!)?.BeginUndoStep();

    /// <summary>
    /// The row already holds the typed value here: the grid writes it through the cell's binding inside EndEdit,
    /// and only then raises this. Everything the change sets off has run by now too, so the step closes on a form
    /// that has settled.
    /// </summary>
    private static void OnCellEndEdit(object? sender, CurrentCellEndEditEventArgs e) => RecorderOf(sender!)?.EndUndoStep();

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
