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
}
