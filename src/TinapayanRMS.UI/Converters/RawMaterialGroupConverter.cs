using System.Globalization;
using System.Windows.Data;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UI.Converters;

/// <summary>
/// The Raw Materials list's group for a row (an SfDataGrid GroupColumnDescription converter, which is
/// handed the whole row): "Ingredients" or "Packaging", which also sorts ingredients first.
/// </summary>
public sealed class RawMaterialGroupConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is RawMaterial { Type: RawMaterialType.Packaging } ? "Packaging" : "Ingredients";

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
