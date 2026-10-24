using System.Windows;
using Syncfusion.UI.Xaml.Grid;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// A grid's headings sit at the bottom of its header row instead of the middle (2026-10-21, at the user's request), so a
/// one-line heading lines up with the last line of a two-line one beside it. Set
/// <c>controls:GridHeaderAlignment.IsBottom="True"</c> on a grid whose headings wrap (<see cref="GridHeaderHeight.LinesProperty"/>).
/// The theme centres a header cell's content, and its sort arrow and filter funnel. A HeaderStyle would replace the
/// theme's style unless BasedOn it, and SfSkinManager only has that style once the window is themed. So each header
/// cell of the grid is set as it loads instead, its local values outranking the theme's style, which it keeps.
/// </summary>
public static class GridHeaderAlignment
{
    public static readonly DependencyProperty IsBottomProperty = DependencyProperty.RegisterAttached(
        "IsBottom", typeof(bool), typeof(GridHeaderAlignment), new PropertyMetadata(false));

    // The parts of a header cell's template that the theme centres beside the heading.
    private static readonly string[] SideParts = ["PART_SortButtonPresenter", "PART_FilterToggleButton"];

    // Once for the whole app, the first time a grid asks: a header cell's Loaded doesn't bubble up to its grid.
    static GridHeaderAlignment() =>
        EventManager.RegisterClassHandler(typeof(GridHeaderCellControl), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnHeaderCellLoaded));

    public static bool GetIsBottom(DependencyObject element) => (bool)element.GetValue(IsBottomProperty);

    public static void SetIsBottom(DependencyObject element, bool value) => element.SetValue(IsBottomProperty, value);

    private static void OnHeaderCellLoaded(object sender, RoutedEventArgs e)
    {
        var cell = (GridHeaderCellControl)sender;
        if (VisualTreeWalk.FindAncestor<SfDataGrid>(cell) is not { } grid || !GetIsBottom(grid))
        {
            return;
        }

        cell.VerticalContentAlignment = VerticalAlignment.Bottom;
        foreach (var name in SideParts)
        {
            if (cell.Template?.FindName(name, cell) is FrameworkElement part)
            {
                part.VerticalAlignment = VerticalAlignment.Bottom;
            }
        }
    }
}
