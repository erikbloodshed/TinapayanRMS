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

    /// <summary>Every unit's Name must be all lowercase (e.g. "bag", not "Bag"), so the picker's display text stays consistent regardless of who added the unit.</summary>
    public static void EnsureNameIsLowercase(string name)
    {
        if (name != name.ToLowerInvariant())
        {
            throw new InvalidOperationException("Unit name must be all lowercase.");
        }
    }
}
