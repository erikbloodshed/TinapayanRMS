using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// Turns recipe lines entered in an ingredient's own units (2 pc of egg, 1 bag of flour) into the
/// grams every weight and cost total is built on. Shared by the sub-assembly and BOM line
/// repositories, which save both what was entered and its grams.
/// </summary>
internal static class IngredientQuantityConverter
{
    /// <summary>
    /// Each line's quantity in grams, in the same order: the quantity itself when it has no unit,
    /// otherwise the quantity times the unit's multiplier (an ingredient's base unit is the gram).
    /// Rejects a unit that isn't one of that line's own ingredient's units.
    /// </summary>
    public static async Task<decimal[]> ToGramsAsync(
        TinapayanRMSDbContext dbContext,
        IReadOnlyList<(int IngredientId, int? UnitId, decimal Quantity)> lines,
        CancellationToken cancellationToken)
    {
        var unitIds = lines.Where(l => l.UnitId is not null).Select(l => l.UnitId!.Value).Distinct().ToList();
        var units = unitIds.Count == 0
            ? []
            : await dbContext.RawMaterialUnits
                .Where(u => unitIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => (u.RawMaterialId, u.BaseUnitsPerUnit), cancellationToken);

        return [.. lines.Select(line =>
        {
            if (line.UnitId is not { } unitId)
            {
                return line.Quantity;
            }

            if (!units.TryGetValue(unitId, out var unit) || unit.RawMaterialId != line.IngredientId)
            {
                throw new InvalidOperationException("A line's unit must be one of that ingredient's own units.");
            }

            return UnitConversionRules.ToBaseQuantity(line.Quantity, unit.BaseUnitsPerUnit);
        })];
    }
}
