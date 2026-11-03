using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Syncfusion.Data;
using Syncfusion.UI.Xaml.Grid;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Keeps a grid's hidden columns, sort and filters between runs (<see cref="GridLayoutStore"/>): puts them back once the grid
/// has loaded, since a filter needs its view, and writes them again whenever a filter, a sort or a column's IsHidden changes.
/// </summary>
public static class GridLayoutKeeping
{
    public static void Keep(SfDataGrid grid, GridLayoutStore store)
    {
        var isApplied = false;
        void Save()
        {
            if (isApplied)
            {
                store.Write(Capture(grid));
            }
        }

        grid.FilterChanged += (_, _) => Save();
        grid.SortColumnsChanged += (_, _) => Save();
        var isHidden = DependencyPropertyDescriptor.FromProperty(GridColumn.IsHiddenProperty, typeof(GridColumn));
        foreach (var column in grid.Columns)
        {
            isHidden.AddValueChanged(column, (_, _) => Save());
        }

        void Loaded(object sender, RoutedEventArgs e)
        {
            grid.Loaded -= Loaded;
            Apply(grid, store.Read());
            isApplied = true;

            // The rows come in after the sort and filters are put back (a screen's load fills its collection), and the grid
            // adds them as they come, unsorted: its view is refreshed once they've all landed.
            if (grid.ItemsSource is INotifyCollectionChanged rows)
            {
                var isRefreshQueued = false;
                rows.CollectionChanged += (_, _) =>
                {
                    if (!isRefreshQueued)
                    {
                        isRefreshQueued = true;
                        grid.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                        {
                            isRefreshQueued = false;
                            grid.View?.Refresh();
                        });
                    }
                };
            }
        }

        grid.Loaded += Loaded;
    }

    private static void Apply(SfDataGrid grid, GridLayout layout)
    {
        if (layout.HiddenColumns is { } hidden)
        {
            foreach (var column in grid.Columns)
            {
                column.IsHidden = hidden.Contains(column.MappingName);
            }
        }

        if (layout.Sorts.Count > 0)
        {
            grid.SortColumnDescriptions.Clear();
            foreach (var sort in layout.Sorts.Where(s => grid.Columns[s.Column] is not null))
            {
                grid.SortColumnDescriptions.Add(new SortColumnDescription
                {
                    ColumnName = sort.Column,
                    SortDirection = sort.IsDescending ? ListSortDirection.Descending : ListSortDirection.Ascending,
                });
            }
        }

        foreach (var filter in layout.Filters)
        {
            if (grid.Columns[filter.Column] is not { } column)
            {
                continue;
            }

            foreach (var condition in filter.Conditions)
            {
                if (Enum.TryParse<FilterType>(condition.FilterType, out var filterType)
                    && Enum.TryParse<PredicateType>(condition.PredicateType, out var predicateType)
                    && Enum.TryParse<FilterBehavior>(condition.FilterBehavior, out var behavior)
                    && Enum.TryParse<ColumnFilter>(condition.FilterMode, out var mode))
                {
                    column.FilterPredicates.Add(new FilterPredicate
                    {
                        FilterType = filterType,
                        PredicateType = predicateType,
                        FilterBehavior = behavior,
                        IsCaseSensitive = condition.IsCaseSensitive,
                        FilterMode = mode,
                        FilterValue = condition.Value,
                    });
                }
            }
        }
    }

    private static GridLayout Capture(SfDataGrid grid) => new(
        [.. grid.Columns.Where(c => c.IsHidden).Select(c => c.MappingName)],
        [.. grid.SortColumnDescriptions.Select(s => new GridSort(s.ColumnName, s.SortDirection == ListSortDirection.Descending))],
        [
            .. grid.Columns
                .Where(c => c.FilterPredicates.Count > 0)
                .Select(c => new GridColumnFilter(c.MappingName,
                [
                    .. c.FilterPredicates.Select(p => new GridFilterCondition(
                        p.FilterType.ToString(), p.PredicateType.ToString(), p.FilterBehavior.ToString(), p.IsCaseSensitive,
                        p.FilterMode.ToString(), p.FilterValue)),
                ])),
        ]);
}
