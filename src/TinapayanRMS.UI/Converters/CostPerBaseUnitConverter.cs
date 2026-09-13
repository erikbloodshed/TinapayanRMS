using System.Globalization;
using System.Windows.Data;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Converters;

/// <summary>
/// The Raw Materials list's cost column: a material's cost per base unit with that unit, e.g.
/// "0.0372 / g" or "5.0000 / pc", since the list mixes ingredients (per gram) and packaging (per
/// its own base unit).
/// </summary>
public sealed class CostPerBaseUnitConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is RawMaterial material
            ? $"{material.CostPerBaseUnit.ToString("#,0.0000", culture)} / {BaseUnitText.AbbreviationOf(material)}"
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
