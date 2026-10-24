using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Grows an SfDataGrid's rows to fit their wrapped text (2026-10-24, for the Production Plan's Mixing Sheet, whose For
/// column lists every product a shared mix is for and wrapped onto a second line in a row one line tall, so the lines
/// overlapped). Set <c>controls:GridAutoRowHeight.IsEnabled="True"</c> on a grid with a <c>TextWrapping="Wrap"</c>
/// column. Each line is as tall as its tallest wrapped cell, and never shorter than the grid's RowHeight.
/// <para>
/// The grid asks each row's height as it lays it out (QueryRowHeight) and measures it with
/// GridColumnSizer.GetAutoRowHeight, the technique from the Sample Browser's DataGridRowHeightBehavior, measuring only
/// the wrapped columns. The grid keeps a height once it has one, so the heights are asked again when the lines change
/// (a screen that merges its rows replaces one in place), the grid's width does, since both change where text wraps, or
/// the rows are sorted: once per batch of changes, after they're through.
/// </para>
/// <para>
/// A grid whose rows open to show details (a DetailsViewDefinition, the Mixing Sheet's ingredients since 2026-10-24) never
/// asks: Syncfusion's RowGenerator.QueryRowHeight answers for it without raising the event, since a details row's height
/// is the nested grid's. Its lines are measured the same way and given their heights directly instead
/// (VisualContainer.RowHeights), passing over the details rows, whose heights the grid keeps itself.
/// </para>
/// </summary>
public static class GridAutoRowHeight
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GridAutoRowHeight), new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty SizerProperty = DependencyProperty.RegisterAttached(
        "Sizer", typeof(RowSizer), typeof(GridAutoRowHeight), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid)
        {
            return;
        }

        (grid.GetValue(SizerProperty) as RowSizer)?.Detach();
        grid.ClearValue(SizerProperty);
        if ((bool)e.NewValue)
        {
            grid.SetValue(SizerProperty, new RowSizer(grid));
        }
    }

    private sealed class RowSizer
    {
        private readonly SfDataGrid _grid;
        private readonly GridRowSizingOptions _options = new();
        private INotifyCollectionChanged? _lines;
        private bool _pending;
        private bool _fitted;

        public RowSizer(SfDataGrid grid)
        {
            _grid = grid;
            grid.QueryRowHeight += OnQueryRowHeight;
            grid.SizeChanged += OnSizeChanged;
            grid.ItemsSourceChanged += OnItemsSourceChanged;
            grid.SortColumnsChanged += OnSortColumnsChanged;
            FollowLines();
        }

        public void Detach()
        {
            _grid.QueryRowHeight -= OnQueryRowHeight;
            _grid.SizeChanged -= OnSizeChanged;
            _grid.ItemsSourceChanged -= OnItemsSourceChanged;
            _grid.SortColumnsChanged -= OnSortColumnsChanged;
            _grid.LayoutUpdated -= OnFirstLayout;
            if (_lines is not null)
            {
                _lines.CollectionChanged -= OnLinesChanged;
                _lines = null;
            }
        }

        /// <summary>The grid's rows open to show details, so it never asks a line's height (see the class's remarks).</summary>
        private bool HasDetails => _grid.DetailsViewDefinition is { Count: > 0 };

        private void OnSortColumnsChanged(object? sender, GridSortColumnsChangedEventArgs e) => AskAgain();

        private void OnQueryRowHeight(object? sender, QueryRowHeightEventArgs e)
        {
            // Lines only: the header row keeps the height GridHeaderHeight gives it.
            if (_grid.ResolveToRecordIndex(e.RowIndex) < 0)
            {
                return;
            }

            // Measured as a cell draws, once one is there to read; until then the rows are asked again later.
            if (!_fitted && !(_fitted = FitSizerToCells()))
            {
                AskAgain();
                return;
            }

            // Every line is given its height, short ones too: a row left unanswered keeps the height it had, so a line
            // whose text was shortened in place stayed tall.
            if (Measure(e.RowIndex) is { } height)
            {
                e.Height = height;
                e.Handled = true;
            }
        }

        /// <summary>What line <paramref name="rowIndex"/> needs for its wrapped cells, never less than the grid's RowHeight; null when it can't be measured.</summary>
        private double? Measure(int rowIndex)
        {
            _options.ExcludeColumns = [.. _grid.Columns.Where(c => c is not GridTextColumn { TextWrapping: TextWrapping.Wrap }).Select(c => c.MappingName)];
            return _grid.GridColumnSizer.GetAutoRowHeight(rowIndex, _options, out var height) ? Math.Max(height, _grid.RowHeight) : null;
        }

        /// <summary>
        /// Gives each line of a grid with details its height directly (see the class's remarks), passing over the details
        /// rows. Before any cell of a line is drawn there's nothing to measure by, so it waits for the grid's first layout.
        /// </summary>
        private void SizeLines(VisualContainer container)
        {
            if (!_fitted && !(_fitted = FitSizerToCells()))
            {
                _grid.LayoutUpdated -= OnFirstLayout;
                _grid.LayoutUpdated += OnFirstLayout;
                return;
            }

            var changed = false;
            for (var row = 0; row < container.RowCount; row++)
            {
                if (_grid.IsInDetailsViewIndex(row) || _grid.ResolveToRecordIndex(row) < 0 || Measure(row) is not { } height)
                {
                    continue;
                }

                if (container.RowHeights[row] != height)
                {
                    container.RowHeights[row] = height;
                    changed = true;
                }
            }

            if (changed)
            {
                container.InvalidateMeasureInfo();
            }
        }

        private void OnFirstLayout(object? sender, EventArgs e)
        {
            if (FirstLineCell(_grid) is not null)
            {
                _grid.LayoutUpdated -= OnFirstLayout;
                AskAgain();
            }
        }

        /// <summary>
        /// Gives the grid's sizer the font, padding and border its cells are drawn with. It measures in its own font
        /// (12 px Segoe UI, a 5,1,5,1 margin) while the theme draws cells at the app's text size (App.ThemeSettings),
        /// so it worked out rows a little short and the second line of text was cut off, as GridHeaderHeight found for
        /// headers. False while no cell of a line is drawn yet.
        /// </summary>
        private bool FitSizerToCells()
        {
            if (FirstLineCell(_grid) is not { } cell)
            {
                return false;
            }

            var text = Descendants(cell).OfType<TextBlock>().FirstOrDefault()?.Margin ?? default;
            var sizer = _grid.GridColumnSizer;
            sizer.FontFamily = cell.FontFamily;
            sizer.FontSize = cell.FontSize;
            sizer.FontWeight = cell.FontWeight;
            sizer.Margin = new Thickness(
                cell.Padding.Left + cell.BorderThickness.Left + text.Left,
                cell.Padding.Top + cell.BorderThickness.Top + text.Top,
                cell.Padding.Right + cell.BorderThickness.Right + text.Right,
                cell.Padding.Bottom + cell.BorderThickness.Bottom + text.Bottom);
            return true;
        }

        /// <summary>A drawn cell of one of the grid's own lines: not of a details grid inside it.</summary>
        private GridCell? FirstLineCell(DependencyObject node) =>
            Descendants(node).OfType<GridCell>().FirstOrDefault(c =>
                c.ColumnBase is { } column && _grid.ResolveToRecordIndex(column.RowIndex) >= 0 && ReferenceEquals(OwningGrid(c), _grid));

        private static SfDataGrid? OwningGrid(DependencyObject node)
        {
            for (var parent = VisualTreeHelper.GetParent(node); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is SfDataGrid grid)
                {
                    return grid;
                }
            }

            return null;
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                yield return child;
                foreach (var descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged)
            {
                AskAgain();
            }
        }

        private void OnItemsSourceChanged(object? sender, GridItemsSourceChangedEventArgs e)
        {
            FollowLines();
            AskAgain();
        }

        private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e) => AskAgain();

        private void FollowLines()
        {
            if (_lines is not null)
            {
                _lines.CollectionChanged -= OnLinesChanged;
            }

            _lines = _grid.ItemsSource as INotifyCollectionChanged;
            if (_lines is not null)
            {
                _lines.CollectionChanged += OnLinesChanged;
            }
        }

        /// <summary>
        /// Has the grid ask every row's height again, or with details gives each line its height, once the changes now
        /// under way are through.
        /// </summary>
        private void AskAgain()
        {
            if (_pending)
            {
                return;
            }

            _pending = true;
            _grid.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                _pending = false;
                if (_grid.GetVisualContainer() is not { } container)
                {
                    return;
                }

                if (HasDetails)
                {
                    SizeLines(container);
                    return;
                }

                for (var row = 0; row < container.RowCount; row++)
                {
                    _grid.InvalidateRowHeight(row);
                }

                container.InvalidateMeasureInfo();
            });
        }
    }
}
