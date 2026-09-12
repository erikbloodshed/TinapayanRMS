using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// Converts between two length units using each unit's own MillimetersPerUnit factor
/// (e.g. a PackagingLine entered in inches for a material whose standard unit is cm).
/// No unit id is special-cased — any unit the shop owner marks IsLengthUnit works.
/// </summary>
public static class LengthConversion
{
    public static decimal Convert(decimal quantity, UnitOfMeasure fromUnit, UnitOfMeasure toUnit)
    {
        if (fromUnit.Id == toUnit.Id)
        {
            return quantity;
        }

        return quantity * MillimetersPerUnitOrThrow(fromUnit) / MillimetersPerUnitOrThrow(toUnit);
    }

    private static decimal MillimetersPerUnitOrThrow(UnitOfMeasure unit) =>
        unit.MillimetersPerUnit ?? throw new InvalidOperationException($"'{unit.Name}' is not a length unit.");
}
