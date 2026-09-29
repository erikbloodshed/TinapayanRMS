using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TinapayanRMS.UI.Converters;

/// <summary>
/// A column that takes an equal share of the row while it's shown, and no width at all while it isn't: one star
/// for true, zero for false. For a Grid whose columns are tiles that come and go (the Raw Materials header's
/// made-only figures), where a plain star column would hold its share open around a collapsed tile and an Auto
/// column would size to its text instead of sharing evenly with the rest. A ConverterParameter, when there is one, is
/// the share while shown ("0.5" for half of one, as the Products details' Quantity Required box takes).
/// </summary>
public sealed class BooleanToStarWidthConverter : IValueConverter
{
    private static readonly GridLength Share = new(1, GridUnitType.Star);
    private static readonly GridLength None = new(0);

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true ? None
        : parameter is string stars ? new GridLength(double.Parse(stars, CultureInfo.InvariantCulture), GridUnitType.Star)
        : Share;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
