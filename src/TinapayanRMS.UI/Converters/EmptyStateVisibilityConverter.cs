using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TinapayanRMS.UI.Converters;

/// <summary>
/// Shows an empty-state hint exactly when there is nothing to show in its place: the value
/// is <see langword="null"/> (no selection, e.g. SelectedRawMaterial) or <see langword="false"/>
/// (a "show detail" flag, e.g. ShowProductDetail). Any other value collapses the hint. The
/// inverse of BooleanToVisibilityConverter for these two shapes of binding, so both a
/// nullable selection and a boolean flag can drive the same kind of hint without a second
/// converter per screen.
/// </summary>
public sealed class EmptyStateVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            null => Visibility.Visible,
            bool flag => flag ? Visibility.Collapsed : Visibility.Visible,
            _ => Visibility.Collapsed,
        };

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
