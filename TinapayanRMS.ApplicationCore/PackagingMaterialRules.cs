using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public static class PackagingMaterialRules
{
    /// <summary>A packaging material's standard unit must be Piece or one of the four length units.</summary>
    public static bool IsValidStandardUnit(int unitOfMeasureId) =>
        unitOfMeasureId == WellKnownUnitOfMeasureIds.Piece || LengthConversion.IsLengthUnit(unitOfMeasureId);

    public static void EnsureValidStandardUnit(int unitOfMeasureId)
    {
        if (!IsValidStandardUnit(unitOfMeasureId))
        {
            throw new InvalidOperationException("A packaging material's standard unit must be Piece or a length unit.");
        }
    }

    /// <summary>
    /// Recomputes CurrentCost from the latest price history entry (null once the
    /// last entry is deleted, reverting the material to unpriced). Call after every
    /// add/update/delete of one of the material's price entries.
    /// </summary>
    public static void ApplyLatestPrice(PackagingMaterial packagingMaterial, PackagingPriceHistory? latest)
    {
        packagingMaterial.CurrentCost = latest?.Price ?? 0m;
    }
}
