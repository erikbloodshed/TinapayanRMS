using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

/// <summary>
/// The four length units are seeded with fixed ids and can never be edited or
/// deleted (TinapayanRMS.Domain.WellKnownUnitOfMeasureIds). Every future
/// edit/delete path for UnitOfMeasure must check this before writing.
/// </summary>
public static class UnitOfMeasureRules
{
    public static bool IsProtected(int unitOfMeasureId) =>
        WellKnownUnitOfMeasureIds.ProtectedLengthUnitIds.Contains(unitOfMeasureId);
}
