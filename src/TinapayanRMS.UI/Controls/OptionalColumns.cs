using System.Windows;
using Syncfusion.UI.Xaml.Grid;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Shows an SfDataGrid's optional columns only while the screen says so. A grid's columns aren't in the visual tree, so
/// they can't bind to the screen; the grid can. Mark each such column <c>controls:OptionalColumns.IsOptional="True"</c>
/// (starting <c>IsHidden="True"</c>, as it's shown only once the grid's flag turns on), and bind the grid's
/// <c>controls:OptionalColumns.Show="{Binding …}"</c>: the marked columns show while it's true. A composite's Products
/// table shows its Packaging Cost column this way, while it includes its products' packaging.
/// </summary>
public static class OptionalColumns
{
    /// <summary>On a column: it shows only while its grid's <see cref="ShowProperty"/> is true. Columns without it are left alone.</summary>
    public static readonly DependencyProperty IsOptionalProperty = DependencyProperty.RegisterAttached(
        "IsOptional", typeof(bool), typeof(OptionalColumns), new PropertyMetadata(false));

    public static bool GetIsOptional(DependencyObject column) => (bool)column.GetValue(IsOptionalProperty);

    public static void SetIsOptional(DependencyObject column, bool value) => column.SetValue(IsOptionalProperty, value);

    /// <summary>On the SfDataGrid: whether its optional columns show.</summary>
    public static readonly DependencyProperty ShowProperty = DependencyProperty.RegisterAttached(
        "Show", typeof(bool), typeof(OptionalColumns), new PropertyMetadata(false, OnShowChanged));

    public static bool GetShow(DependencyObject grid) => (bool)grid.GetValue(ShowProperty);

    public static void SetShow(DependencyObject grid, bool value) => grid.SetValue(ShowProperty, value);

    private static void OnShowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid)
        {
            return;
        }

        Apply(grid);

        // The binding can land before the XAML's columns are added: they're shown or hidden again once they're there.
        if (!grid.IsLoaded)
        {
            grid.Loaded -= OnLoaded;
            grid.Loaded += OnLoaded;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var grid = (SfDataGrid)sender;
        grid.Loaded -= OnLoaded;
        Apply(grid);
    }

    private static void Apply(SfDataGrid grid)
    {
        var show = GetShow(grid);
        foreach (var column in grid.Columns.Where(GetIsOptional))
        {
            column.IsHidden = !show;
        }
    }
}
