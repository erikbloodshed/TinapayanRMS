using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public static class ProductRules
{
    public static void EnsureValidUsages(IReadOnlyList<(int SubAssemblyId, decimal WeightPerPiece)> usages)
    {
        if (usages.Count == 0)
        {
            throw new InvalidOperationException("A product needs at least one sub-assembly usage.");
        }

        if (usages.Any(u => u.WeightPerPiece <= 0))
        {
            throw new InvalidOperationException("A sub-assembly usage's weight per piece must be positive.");
        }

        if (usages.Select(u => u.SubAssemblyId).Distinct().Count() != usages.Count)
        {
            throw new InvalidOperationException("A product can't use the same sub-assembly more than once.");
        }
    }

    /// <summary>Pieces the sub-assembly's current batch yields at this usage's weight per piece. Informational only — never stored, and expected to differ across a product's usages.</summary>
    public static decimal CalculateYield(decimal subAssemblyTotalWeight, decimal weightPerPiece) =>
        subAssemblyTotalWeight / weightPerPiece;

    /// <summary>This usage's share of the sub-assembly's cost, per finished piece.</summary>
    public static decimal CalculateUsageCostPerPiece(decimal subAssemblyTotalCost, decimal weightPerPiece, decimal subAssemblyTotalWeight) =>
        subAssemblyTotalCost * weightPerPiece / subAssemblyTotalWeight;

    public static void EnsureValidPackagingLines(IReadOnlyList<(int PackagingMaterialId, decimal QtyPerPiece)> lines)
    {
        if (lines.Any(l => l.QtyPerPiece <= 0))
        {
            throw new InvalidOperationException("A packaging line's quantity per piece must be positive.");
        }

        if (lines.Select(l => l.PackagingMaterialId).Distinct().Count() != lines.Count)
        {
            throw new InvalidOperationException("A product can't use the same packaging material more than once — combine it into a single line instead.");
        }
    }

    /// <summary>A packaging line's entry unit must be the material's own standard unit, or — only when that standard unit is itself a length unit — any of the four length units.</summary>
    public static void EnsureValidEntryUnit(int packagingMaterialStandardUnitId, int entryUnitId)
    {
        if (entryUnitId == packagingMaterialStandardUnitId)
        {
            return;
        }

        if (LengthConversion.IsLengthUnit(packagingMaterialStandardUnitId) && LengthConversion.IsLengthUnit(entryUnitId))
        {
            return;
        }

        throw new InvalidOperationException("A packaging line's entry unit must be the material's standard unit, or another length unit if the material is measured in length.");
    }

    /// <summary>Derives QtyPerPiece and PiecesPerPackagingUnit — exact reciprocals of each other — from whichever one the user actually typed.</summary>
    public static (decimal QtyPerPiece, decimal PiecesPerPackagingUnit) ReconcileReciprocal(
        PackagingLineAuthoredField authoredField, decimal authoredValue) =>
        authoredField switch
        {
            PackagingLineAuthoredField.QtyPerPiece => (authoredValue, 1m / authoredValue),
            PackagingLineAuthoredField.PiecesPerPackagingUnit => (1m / authoredValue, authoredValue),
            _ => throw new ArgumentOutOfRangeException(nameof(authoredField)),
        };

    /// <summary>Cost of one packaging line per finished piece, converting the entry-unit quantity to the material's standard unit first (a no-op when they're the same unit).</summary>
    public static decimal CalculatePackagingLineCostPerPiece(
        decimal packagingMaterialCurrentCost, decimal purchaseUnitQty, decimal qtyPerPiece, int entryUnitId, int standardUnitId)
    {
        var qtyInStandardUnit = LengthConversion.Convert(qtyPerPiece, entryUnitId, standardUnitId);
        return packagingMaterialCurrentCost / purchaseUnitQty * qtyInStandardUnit;
    }

    /// <summary>A product's total material cost per piece: every sub-assembly usage's cost-per-piece, plus every packaging line's cost-per-piece.</summary>
    public static decimal CalculateMaterialCostPerPiece(IReadOnlyList<decimal> usageCostsPerPiece, IReadOnlyList<decimal> packagingCostsPerPiece) =>
        usageCostsPerPiece.Sum() + packagingCostsPerPiece.Sum();
}
