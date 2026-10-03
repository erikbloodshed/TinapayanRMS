using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.TreeGrid;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Every SfDataGrid's and SfTreeGrid's header row is as tall as its text needs (2026-09-27, at the user's request). The Windows11Light
/// theme's grid style fixes it at 24px, sized for its own 12px body text; since the fonts went up to 14px (App's theme
/// settings), a header's text needs 19px plus the header cell's 3px padding above and below and its 1px bottom border,
/// so descenders were cut off ("Ingredient" read "Inqredient").
///
/// The height is worked out from a header cell itself: its font and padding are the theme's (BodyTextStyle, 5,3,5,3),
/// which the grid's own FontSize doesn't follow (it stays 12), so it keeps fitting if the fonts change again. A grid has
/// header cells only once it's laid out visibly, so a hidden one (a table behind another, a drop-down's list) is fitted
/// the first time it's sized, and each grid once. A local value, since the theme sets the height through a Style, which
/// outranks a type default; a HeaderRowHeight set in XAML still wins. Registered once for the whole app, from App's
/// static constructor, so the grids inside the multi-column drop-downs get it too. A grid whose headings wrap (a
/// HeaderTemplate with TextWrapping, as Reports' Product Cost Summary has) sets <see cref="LinesProperty"/> to the lines
/// they can take.
/// </summary>
public static class GridHeaderHeight
{
    /// <summary>The theme's own header height, which a header never goes below.</summary>
    private const double ThemeHeight = 24;

    /// <summary>Set once a grid's header has been fitted, so it's fitted only once, and so a height set in XAML can be told from ours.</summary>
    private static readonly DependencyProperty IsFittedProperty = DependencyProperty.RegisterAttached(
        "IsFitted", typeof(bool), typeof(GridHeaderHeight), new PropertyMetadata(false));

    /// <summary>How many lines of heading the header row fits: 1 unless a grid's headings wrap.</summary>
    public static readonly DependencyProperty LinesProperty = DependencyProperty.RegisterAttached(
        "Lines", typeof(int), typeof(GridHeaderHeight), new PropertyMetadata(1));

    private static bool _registered;

    public static int GetLines(DependencyObject element) => (int)element.GetValue(LinesProperty);

    public static void SetLines(DependencyObject element, int value) => element.SetValue(LinesProperty, value);

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        foreach (var gridType in (Type[])[typeof(SfDataGrid), typeof(SfTreeGrid)])
        {
            EventManager.RegisterClassHandler(gridType, FrameworkElement.LoadedEvent, new RoutedEventHandler(FitHeader));
            EventManager.RegisterClassHandler(gridType, FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(FitHeader));
        }
    }

    /// <summary>
    /// A header row that fits <paramref name="lines"/> lines of <paramref name="fontFamily"/> at <paramref name="fontSize"/>
    /// inside a cell with <paramref name="padding"/> and <paramref name="border"/>, never shorter than the theme's 24px.
    /// </summary>
    public static double HeightFor(FontFamily fontFamily, double fontSize, Thickness padding, Thickness border, int lines = 1) =>
        Math.Max(ThemeHeight, Math.Ceiling(fontSize * fontFamily.LineSpacing * lines) + padding.Top + padding.Bottom + border.Top + border.Bottom);

    private static void FitHeader(object sender, RoutedEventArgs e)
    {
        var grid = (SfGridBase)sender;
        if ((bool)grid.GetValue(IsFittedProperty)
            || grid.ReadLocalValue(SfGridBase.HeaderRowHeightProperty) != DependencyProperty.UnsetValue
            || FirstHeaderCell(grid) is not { } cell)
        {
            return;
        }

        grid.SetValue(IsFittedProperty, true);
        grid.HeaderRowHeight = HeightFor(cell.FontFamily, cell.FontSize, cell.Padding, cell.BorderThickness, GetLines(grid));
    }

    /// <summary>A data grid's header cell, or a tree grid's (a TreeGridHeaderCell, which isn't a GridHeaderCellControl).</summary>
    private static Control? FirstHeaderCell(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if ((child is GridHeaderCellControl or TreeGridHeaderCell ? (Control)child : FirstHeaderCell(child)) is { } cell)
            {
                return cell;
            }
        }

        return null;
    }
}
