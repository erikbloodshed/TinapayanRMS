using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Fills the space under an SfDataGrid's last row with empty rows, so a short list still reads as a table down
/// to the grid's bottom edge. Set <c>controls:GridFillerRows.IsEnabled="True"</c> on a grid with a height to fill
/// (one in a star-sized row or with a fixed Height, not one sized to its rows).
/// The rows are GridUnBoundRows, recalculated when the grid is loaded, resized, its records change, or a group
/// is expanded or collapsed (the technique from the SfDataGrid_Demo sample), with these differences, read from
/// the decompiled Syncfusion 34.2.7:
/// <list type="bullet">
/// <item>They're added with ShowBelowSummary, which puts them after the bottom add-new row; without it they come
/// before it, and "Click here to add an ingredient" would sink below them.</item>
/// <item>Those rows are frozen footer rows. With a short body they sit right under it (the scroll axis's
/// ViewSize is the smaller of its render size and total extent), but once the rows outgrow the grid they'd be
/// pinned over its last rows. So rows that no longer fit are removed at once, when records are added, a group
/// is expanded or the grid shrinks; adding them waits for the change to settle.</item>
/// <item>The space is measured on the grid's row scroll axis (render size against total extent), which counts
/// the header, group captions and the add-new row as they're drawn, rather than by counting records.</item>
/// <item>Only the difference is added or removed, never a Clear(): UnBoundRows' Reset handling miscounts its
/// rows by position.</item>
/// <item>Every trigger funnels into one pending recalculation, so a burst of record changes adds rows once.</item>
/// <item>The rows can't be clicked, selected or edited: a click on one would otherwise deselect the record, which
/// clears the form beside the list, and in a table typed into (EditTrigger OnTap) it would start editing the current
/// cell wherever it is. A press on one never reaches the grid, and the keyboard can't move onto one.</item>
/// <item>Changing unbound rows reaches the grid's selection (GridSelectionController.ProcessUnBoundRowChanged):
/// it ends an edit of an existing row without committing it, and adding one while the current cell is on the row
/// above the add-new row moves the current cell onto it. So nothing changes while a row is being edited (it's
/// tried again shortly after), and a current cell moved by an addition is put back.</item>
/// </list>
/// </summary>
public static class GridFillerRows
{
    /// <summary>How long a change has to settle before rows are added (a window drag grows the grid in steps), and how soon to try again during an edit.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GridFillerRows), new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(FillerState), typeof(GridFillerRows), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>
    /// How many whole rows of <paramref name="rowHeight"/> fit in the space <paramref name="contentHeight"/> leaves
    /// in <paramref name="viewportHeight"/>. A part of a row is left empty, so filler rows never add a scrollbar.
    /// </summary>
    public static int FillerRowCount(double viewportHeight, double contentHeight, double rowHeight)
    {
        if (rowHeight <= 0 || !double.IsFinite(viewportHeight) || !double.IsFinite(contentHeight))
        {
            return 0;
        }

        // A hair over a whole number of rows, from fractional layout sizes, still counts as that many.
        return Math.Max(0, (int)Math.Floor(((viewportHeight - contentHeight) / rowHeight) + 1e-6));
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid)
        {
            return;
        }

        grid.Loaded -= OnLoaded;
        grid.Unloaded -= OnUnloaded;
        grid.SizeChanged -= OnSizeChanged;
        grid.IsVisibleChanged -= OnIsVisibleChanged;
        grid.ItemsSourceChanged -= OnItemsSourceChanged;
        grid.GroupExpanded -= OnGroupExpanded;
        grid.GroupCollapsed -= OnGroupCollapsed;
        grid.SelectionChanging -= OnSelectionChanging;
        grid.CurrentCellActivating -= OnCurrentCellActivating;
        grid.PreviewMouseDown -= OnPreviewMouseDown;

        if (GetState(grid) is { } previous)
        {
            previous.StopWatching();
            previous.RemoveRows();
            grid.ClearValue(StateProperty);
        }

        if (!(bool)e.NewValue)
        {
            return;
        }

        var state = new FillerState(grid);
        grid.SetValue(StateProperty, state);
        grid.Loaded += OnLoaded;
        grid.Unloaded += OnUnloaded;
        grid.SizeChanged += OnSizeChanged;
        grid.IsVisibleChanged += OnIsVisibleChanged;
        grid.ItemsSourceChanged += OnItemsSourceChanged;
        grid.GroupExpanded += OnGroupExpanded;
        grid.GroupCollapsed += OnGroupCollapsed;
        grid.SelectionChanging += OnSelectionChanging;
        grid.CurrentCellActivating += OnCurrentCellActivating;
        grid.PreviewMouseDown += OnPreviewMouseDown;

        if (grid.IsLoaded)
        {
            state.WatchView();
            state.Schedule();
        }
    }

    private static FillerState? GetState(DependencyObject grid) => grid.GetValue(StateProperty) as FillerState;

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (GetState((DependencyObject)sender) is { } state)
        {
            state.WatchView();
            state.Schedule();
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e) => GetState((DependencyObject)sender)?.StopWatching();

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => GetState((DependencyObject)sender)?.Resized(e.NewSize.Height);

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            GetState((DependencyObject)sender)?.Schedule();
        }
    }

    private static void OnItemsSourceChanged(object? sender, GridItemsSourceChangedEventArgs e)
    {
        if (sender is DependencyObject grid && GetState(grid) is { } state)
        {
            state.WatchView();
            state.Schedule();
        }
    }

    private static void OnGroupExpanded(object? sender, GroupChangedEventArgs e)
    {
        if (sender is DependencyObject grid)
        {
            GetState(grid)?.ContentGrew();
        }
    }

    private static void OnGroupCollapsed(object? sender, GroupChangedEventArgs e)
    {
        if (sender is DependencyObject grid)
        {
            GetState(grid)?.Schedule();
        }
    }

    /// <summary>A press on a filler row goes no further, so the grid neither selects nor starts an edit for it.</summary>
    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is SfDataGrid grid
            && GetState(grid) is { } state
            && grid.GetVisualContainer() is { } container
            && state.IsFillerRowIndex(container.PointToCellRowColumnIndex(e.GetPosition(container), false).RowIndex))
        {
            e.Handled = true;
        }
    }

    private static void OnSelectionChanging(object? sender, GridSelectionChangingEventArgs e)
    {
        if (sender is DependencyObject grid
            && GetState(grid) is { } state
            && e.AddedItems.OfType<GridRowInfo>().Any(r => r.IsUnBoundRow && state.IsFillerRowIndex(r.RowIndex)))
        {
            e.Cancel = true;
        }
    }

    private static void OnCurrentCellActivating(object? sender, CurrentCellActivatingEventArgs e)
    {
        if (sender is DependencyObject grid
            && GetState(grid) is { } state
            && state.IsFillerRowIndex(e.CurrentRowColumnIndex.RowIndex))
        {
            e.Cancel = true;
        }
    }

    /// <summary>One grid's filler rows, and what keeps them sized: its view's subscription, the settle delay and the pending recalculation.</summary>
    private sealed class FillerState
    {
        private readonly SfDataGrid _grid;
        private readonly DispatcherTimer _settleTimer;
        private readonly List<GridUnBoundRow> _rows = [];
        private INotifyCollectionChanged? _view;
        private bool _pending;
        private double _lastHeight;

        public FillerState(SfDataGrid grid)
        {
            _grid = grid;
            _settleTimer = new DispatcherTimer(DispatcherPriority.Background, grid.Dispatcher) { Interval = SettleDelay };
            _settleTimer.Tick += (_, _) =>
            {
                _settleTimer.Stop();
                Schedule();
            };
        }

        /// <summary>
        /// Whether the grid's row at <paramref name="rowIndex"/> is one of the filler rows. They're the grid's last rows
        /// (bottom unbound rows shown below the summaries come after everything else, and the grid has no others), so
        /// they're found by position: an unbound row's own RowIndex isn't kept up to date as records come and go, and
        /// SfDataGrid.GetUnBoundRow, which matches it first, can take a group caption or a record for a filler row.
        /// </summary>
        public bool IsFillerRowIndex(int rowIndex)
        {
            var rowCount = _grid.GetVisualContainer()?.RowCount ?? 0;
            return _rows.Count > 0 && rowIndex >= rowCount - _rows.Count && rowIndex < rowCount;
        }

        /// <summary>Follows the grid's current view: records added, removed or filtered out change the space left.</summary>
        public void WatchView()
        {
            var view = _grid.View as INotifyCollectionChanged;
            if (ReferenceEquals(view, _view))
            {
                return;
            }

            if (_view is not null)
            {
                _view.CollectionChanged -= OnViewChanged;
            }

            _view = view;
            if (_view is not null)
            {
                _view.CollectionChanged += OnViewChanged;
            }
        }

        public void StopWatching()
        {
            _settleTimer.Stop();
            if (_view is not null)
            {
                _view.CollectionChanged -= OnViewChanged;
                _view = null;
            }
        }

        public void Resized(double height)
        {
            var shrank = height < _lastHeight;
            _lastHeight = height;
            if (shrank)
            {
                Apply(allowAdding: false);
            }
            else
            {
                _settleTimer.Stop();
                _settleTimer.Start();
            }
        }

        /// <summary>The rows took more room (records added, a group expanded): what no longer fits goes now, and the rest is recalculated after.</summary>
        public void ContentGrew()
        {
            Apply(allowAdding: false);
            Schedule();
        }

        /// <summary>Recalculates once, after layout, however many triggers ask before it runs.</summary>
        public void Schedule()
        {
            if (_pending)
            {
                return;
            }

            _pending = true;
            _grid.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _pending = false;
                Apply(allowAdding: true);
            }));
        }

        public void RemoveRows()
        {
            while (_rows.Count > 0)
            {
                RemoveLastRow();
            }
        }

        private void OnViewChanged(object? sender, NotifyCollectionChangedEventArgs e) => ContentGrew();

        private void Apply(bool allowAdding)
        {
            if (!_grid.IsLoaded || !_grid.IsVisible || _grid.GetVisualContainer()?.ScrollRows is not PixelScrollAxis rows)
            {
                return;
            }

            // A change now would end the row's edit without committing it: try again once it's over.
            if (_grid.View is IEditableCollectionView { IsEditingItem: true })
            {
                _settleTimer.Stop();
                _settleTimer.Start();
                return;
            }

            var rowHeight = _grid.RowHeight;
            var wanted = FillerRowCount(rows.RenderSize, rows.TotalExtent - (_rows.Count * rowHeight), rowHeight);

            while (_rows.Count > wanted)
            {
                RemoveLastRow();
            }

            if (!allowAdding || _rows.Count >= wanted)
            {
                return;
            }

            var currentCell = _grid.SelectionController?.CurrentCellManager?.CurrentRowColumnIndex ?? new RowColumnIndex(-1, -1);
            while (_rows.Count < wanted)
            {
                var row = new GridUnBoundRow { Position = UnBoundRowsPosition.Bottom, ShowBelowSummary = true };
                _rows.Add(row);
                _grid.UnBoundRows.Add(row);
            }

            // With the current cell on the last line, an addition moves it onto the add-new row.
            if (currentCell.RowIndex >= 0
                && _grid.SelectionController?.CurrentCellManager is { } manager
                && manager.CurrentRowColumnIndex != currentCell)
            {
                _grid.MoveCurrentCell(currentCell, needToClearSelection: false);
            }
        }

        private void RemoveLastRow()
        {
            var row = _rows[^1];
            _rows.RemoveAt(_rows.Count - 1);
            _grid.UnBoundRows.Remove(row);
        }
    }
}
