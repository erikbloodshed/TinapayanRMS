using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TinapayanRMS.UI.Converters;

/// <summary>
/// Binds a number box's <c>double</c> value (DoubleTextBox, PercentTextBox) to a <c>decimal</c> property, so 28.35
/// typed is 28.35. Since .NET 11 a <c>double</c> converts to a <c>decimal</c> at its exact binary value (28.35 becomes
/// 28.349999999999997868371792719), and WPF's own conversion does just that. This goes through the number's shortest
/// round-trip text instead, the way the grid's NumericColumn does (ParsingMode Decimal). A property that isn't a
/// <c>decimal</c> gets the value as it is.
/// </summary>
public sealed class DecimalValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal number ? (double)number : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double number || (Nullable.GetUnderlyingType(targetType) ?? targetType) != typeof(decimal))
        {
            return value;
        }

        return ToDecimal(number) is { } converted ? converted : DependencyProperty.UnsetValue;
    }

    /// <summary>
    /// <paramref name="number"/> as the decimal its shortest round-trip text reads (0.1 is 0.1), or null when it's not
    /// a finite number or is too big for a decimal.
    /// </summary>
    public static decimal? ToDecimal(double number) =>
        double.IsFinite(number)
        && decimal.TryParse(number.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var converted)
            ? converted
            : null;
}
