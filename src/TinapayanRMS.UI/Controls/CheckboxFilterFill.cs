using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Syncfusion.UI.Xaml.Grid;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// A grid's filter pop-up shows its values across its whole width. The Windows11Light theme lays out the list of values
/// (CheckboxFilterControl) in a column fixed at 188px, whatever the pop-up's width (FilterPopupWidth, 300 by default),
/// so a value longer than that was cut off and the list scrolled sideways: "Research and Development", and two products
/// that both read "Special Ensaymada Sma". Set <c>controls:CheckboxFilterFill.IsEnabled="True"</c> on a grid that
/// filters, with a FilterPopupWidth in its FilterPopupStyle that fits its longest value. The column is widened each time
/// the pop-up fills with values, which is when the grid has its template ready; a theme without that column is left alone.
/// </summary>
public static class CheckboxFilterFill
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(CheckboxFilterFill), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid)
        {
            return;
        }

        grid.FilterItemsPopulated -= OnFilterItemsPopulated;
        if ((bool)e.NewValue)
        {
            grid.FilterItemsPopulated += OnFilterItemsPopulated;
        }
    }

    private static void OnFilterItemsPopulated(object? sender, GridFilterItemsPopulatedEventArgs e) => Fill(e.FilterControl);

    /// <summary>Widens <paramref name="filterControl"/>'s list of values to fill it, if the theme fixed its width.</summary>
    public static void Fill(GridFilterControl filterControl)
    {
        filterControl.ApplyTemplate();
        if (filterControl.Template?.FindName("PART_CheckboxFilterControl", filterControl) is not CheckboxFilterControl list)
        {
            return;
        }

        list.ApplyTemplate();
        if (VisualTreeHelper.GetChildrenCount(list) > 0
            && VisualTreeHelper.GetChild(list, 0) is Grid { ColumnDefinitions: [.., var values] }
            && values.Width.IsAbsolute)
        {
            values.Width = new GridLength(1, GridUnitType.Star);
        }
    }
}
