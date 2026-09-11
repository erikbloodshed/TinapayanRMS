using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

/// <summary>
/// Converts between the four protected length units. UnitOfMeasure stores no
/// conversion factors itself, so this is the one place cm/mm/inch/m math happens
/// (e.g. a PackagingLine entered in inches for a material whose standard unit is cm).
/// </summary>
public static class LengthConversion
{
    private static readonly IReadOnlyDictionary<int, decimal> MillimetersPerUnit = new Dictionary<int, decimal>
    {
        [WellKnownUnitOfMeasureIds.Millimeter] = 1m,
        [WellKnownUnitOfMeasureIds.Centimeter] = 10m,
        [WellKnownUnitOfMeasureIds.Inch] = 25.4m,
        [WellKnownUnitOfMeasureIds.Meter] = 1000m,
    };

    public static bool IsLengthUnit(int unitOfMeasureId) => MillimetersPerUnit.ContainsKey(unitOfMeasureId);

    public static decimal Convert(decimal quantity, int fromUnitOfMeasureId, int toUnitOfMeasureId)
    {
        if (fromUnitOfMeasureId == toUnitOfMeasureId)
        {
            return quantity;
        }

        var millimeters = quantity * MillimetersPerUnitOrThrow(fromUnitOfMeasureId);
        return millimeters / MillimetersPerUnitOrThrow(toUnitOfMeasureId);
    }

    private static decimal MillimetersPerUnitOrThrow(int unitOfMeasureId) =>
        MillimetersPerUnit.TryGetValue(unitOfMeasureId, out var factor)
            ? factor
            : throw new ArgumentOutOfRangeException(
                nameof(unitOfMeasureId), unitOfMeasureId, "Not one of the four protected length units.");
}
