using System.Windows;
using Syncfusion.UI.Xaml.Grid;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Fills the space under an SfDataGrid's last row with the lines of empty rows, so a short list still reads as a table
/// down to the grid's bottom edge. Set <c>controls:GridFillerRows.IsEnabled="True"</c> on a grid with a height to fill
/// (one in a star-sized row or with a fixed Height, not one sized to its rows).
///
/// The rows are only drawn (<see cref="GridFillerLayer"/>), not added to the grid (2026-10-15). Until then they were
/// GridUnBoundRows, the technique from the SfDataGrid_Demo sample, and so live rows the grid selected, edited, moved its
/// current cell onto and froze as footers: most of the code was guards against that, and they still crashed the app
/// twice and drew a second current-cell outline. Drawn, the grid never knows they're there.
/// </summary>
public static class GridFillerRows
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GridFillerRows), new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty LayerProperty = DependencyProperty.RegisterAttached(
        "Layer", typeof(GridFillerLayer), typeof(GridFillerRows), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>
    /// How many rows of <paramref name="rowHeight"/> it takes to fill the space <paramref name="contentHeight"/> leaves
    /// in <paramref name="viewportHeight"/>: the last is cut off by the grid's bottom edge when only part of it fits, as
    /// the grid's own last row is. (Until 2026-10-16 it was whole rows only, from when the rows were the grid's own and a
    /// part of one would have made it scroll; drawn, they can't.)
    /// </summary>
    public static int FillerRowCount(double viewportHeight, double contentHeight, double rowHeight)
    {
        if (rowHeight <= 0 || !double.IsFinite(viewportHeight) || !double.IsFinite(contentHeight))
        {
            return 0;
        }

        // A hair over a whole number of rows, from fractional layout sizes, doesn't start another.
        return Math.Max(0, (int)Math.Ceiling(((viewportHeight - contentHeight) / rowHeight) - 1e-6));
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid)
        {
            return;
        }

        grid.Loaded -= OnLoaded;
        grid.Unloaded -= OnUnloaded;
        if (GetLayer(grid) is { } previous)
        {
            previous.Remove();
            grid.ClearValue(LayerProperty);
        }

        if (!(bool)e.NewValue)
        {
            return;
        }

        var layer = new GridFillerLayer(grid);
        grid.SetValue(LayerProperty, layer);
        grid.Loaded += OnLoaded;
        grid.Unloaded += OnUnloaded;
        if (grid.IsLoaded)
        {
            layer.StartWatching();
        }
    }

    private static GridFillerLayer? GetLayer(DependencyObject grid) => grid.GetValue(LayerProperty) as GridFillerLayer;

    private static void OnLoaded(object sender, RoutedEventArgs e) => GetLayer((DependencyObject)sender)?.StartWatching();

    private static void OnUnloaded(object sender, RoutedEventArgs e) => GetLayer((DependencyObject)sender)?.StopWatching();
}
