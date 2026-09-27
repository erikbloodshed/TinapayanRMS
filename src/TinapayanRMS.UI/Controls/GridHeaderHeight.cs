using System.Windows;
using System.Windows.Media;
using Syncfusion.UI.Xaml.Grid;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Every SfDataGrid's header row is as tall as its text needs (2026-09-27, at the user's request). The Windows11Light
/// theme's grid style fixes it at 24px, sized for its own 12px body text; since the fonts went up to 14px (App's theme
/// settings), a header's text needs 19px plus the header cell's 3px padding above and below and its 1px bottom border,
/// so descenders were cut off ("Ingredient" read "Inqredient").
///
/// The height is worked out from a header cell itself: its font and padding are the theme's (BodyTextStyle, 5,3,5,3),
/// which the grid's own FontSize doesn't follow (it stays 12), so it keeps fitting if the fonts change again. A grid has
/// header cells only once it's laid out visibly, so a hidden one (a table behind another, a drop-down's list) is fitted
/// the first time it's sized, and each grid once. A local value, since the theme sets the height through a Style, which
/// outranks a type default; a HeaderRowHeight set in XAML still wins. Registered once for the whole app, from App's
/// static constructor, so the grids inside the multi-column drop-downs get it too.
/// </summary>
public static class GridHeaderHeight
{
    /// <summary>The theme's own header height, which a header never goes below.</summary>
    private const double ThemeHeight = 24;

    /// <summary>Set once a grid's header has been fitted, so it's fitted only once, and so a height set in XAML can be told from ours.</summary>
    private static readonly DependencyProperty IsFittedProperty = DependencyProperty.RegisterAttached(
        "IsFitted", typeof(bool), typeof(GridHeaderHeight), new PropertyMetadata(false));

    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        EventManager.RegisterClassHandler(typeof(SfDataGrid), FrameworkElement.LoadedEvent, new RoutedEventHandler(FitHeader));
        EventManager.RegisterClassHandler(typeof(SfDataGrid), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(FitHeader));
    }

    /// <summary>
    /// A header row that fits one line of <paramref name="fontFamily"/> at <paramref name="fontSize"/> inside a cell with
    /// <paramref name="padding"/> and <paramref name="border"/>, never shorter than the theme's 24px.
    /// </summary>
    public static double HeightFor(FontFamily fontFamily, double fontSize, Thickness padding, Thickness border) =>
        Math.Max(ThemeHeight, Math.Ceiling(fontSize * fontFamily.LineSpacing) + padding.Top + padding.Bottom + border.Top + border.Bottom);

    private static void FitHeader(object sender, RoutedEventArgs e)
    {
        var grid = (SfDataGrid)sender;
        if ((bool)grid.GetValue(IsFittedProperty)
            || grid.ReadLocalValue(SfGridBase.HeaderRowHeightProperty) != DependencyProperty.UnsetValue
            || FirstHeaderCell(grid) is not { } cell)
        {
            return;
        }

        grid.SetValue(IsFittedProperty, true);
        grid.HeaderRowHeight = HeightFor(cell.FontFamily, cell.FontSize, cell.Padding, cell.BorderThickness);
    }

    private static GridHeaderCellControl? FirstHeaderCell(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if ((child as GridHeaderCellControl ?? FirstHeaderCell(child)) is { } cell)
            {
                return cell;
            }
        }

        return null;
    }
}
