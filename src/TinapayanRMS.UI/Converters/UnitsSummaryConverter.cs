using System.Collections;
using System.Globalization;
using System.Windows.Data;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.Converters;

/// <summary>
/// The list's "Alt Units" text: an item's alternative units with their multipliers, e.g.
/// "bag (25,000), pack (1,000)", or "—" when it has none.
/// </summary>
public sealed class UnitsSummaryConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = (value as IEnumerable ?? Array.Empty<object>())
            .Cast<object>()
            .Select(unit => unit switch
            {
                IngredientUnit u => $"{u.UnitOfMeasure?.Name} ({u.GramsPerUnit.ToString("#,0.####", culture)})",
                PackagingMaterialUnit u => $"{u.UnitOfMeasure?.Name} ({u.BaseUnitsPerUnit.ToString("#,0.####", culture)})",
                _ => null,
            })
            .OfType<string>()
            .ToList();

        return parts.Count == 0 ? "—" : string.Join(", ", parts);
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
