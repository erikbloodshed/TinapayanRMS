using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Cells;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Sets a number column's text in semibold on the rows where a <see langword="bool"/> property of the row is true — the
/// typed figure of a pair, as the Products screen's piece size once did. Turn it on for the grid with
/// <c>controls:GridCellEmphasis.IsEnabled="True"</c>, and name the property on the column:
/// <c>controls:GridCellEmphasis.When="IsPercentTyped"</c>.
/// <para>
/// It's a renderer rather than a cell style: a <c>CellStyle</c> replaces the theme's own <c>GridCell</c> style, and
/// reaching the theme's to base one on would mean merging its dictionary here, beside the accent colours the app sets
/// at startup. The weight is a binding, so it follows the row as it changes.
/// </para>
/// </summary>
public static class GridCellEmphasis
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GridCellEmphasis), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty WhenProperty = DependencyProperty.RegisterAttached(
        "When", typeof(string), typeof(GridCellEmphasis), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static string? GetWhen(DependencyObject element) => (string?)element.GetValue(WhenProperty);

    public static void SetWhen(DependencyObject element, string? value) => element.SetValue(WhenProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid || !(bool)e.NewValue)
        {
            return;
        }

        grid.CellRenderers.Remove(EmphasisNumericRenderer.CellType);
        grid.CellRenderers.Add(EmphasisNumericRenderer.CellType, new EmphasisNumericRenderer());
    }
}

/// <summary>
/// The number column's renderer, binding its text's weight to the column's <see cref="GridCellEmphasis.WhenProperty"/>.
/// One text block serves many cells as rows recycle, and the grid re-initializes it each time
/// (<c>OnUpdateDisplayBinding</c> calls <see cref="OnInitializeDisplayElement"/>), so a column without it is cleared.
/// </summary>
public sealed class EmphasisNumericRenderer : GridCellNumericRenderer
{
    public const string CellType = "Numeric";

    public override void OnInitializeDisplayElement(DataColumnBase dataColumn, TextBlock uiElement, object dataContext)
    {
        base.OnInitializeDisplayElement(dataColumn, uiElement, dataContext);
        if (dataColumn?.GridColumn is null || uiElement is null)
        {
            return;
        }

        if (GridCellEmphasis.GetWhen(dataColumn.GridColumn) is { Length: > 0 } propertyName)
        {
            uiElement.SetBinding(TextBlock.FontWeightProperty, new Binding(propertyName) { Source = dataContext, Converter = SemiBoldWhenTrue.Instance });
        }
        else
        {
            BindingOperations.ClearBinding(uiElement, TextBlock.FontWeightProperty);
        }
    }

    private sealed class SemiBoldWhenTrue : IValueConverter
    {
        public static SemiBoldWhenTrue Instance { get; } = new();

        public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? FontWeights.SemiBold : FontWeights.Normal;

        public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
