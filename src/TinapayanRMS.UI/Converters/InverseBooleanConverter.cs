using System.Globalization;
using System.Windows.Data;

namespace TinapayanRMS.UI.Converters;

/// <summary>True for false and false for true, both ways: a column's tick box, ticked while the column isn't hidden.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
