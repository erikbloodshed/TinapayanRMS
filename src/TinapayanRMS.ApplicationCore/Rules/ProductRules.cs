using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Rules;

public static class ProductRules
{
    /// <summary>
    /// A product's whole set of usages: an empty set is valid (PLAN.md doesn't require a product to
    /// have a usage at all times), but every typed piece size must be positive, and a product uses each
    /// sub-assembly once.
    /// </summary>
    public static void EnsureValidUsages(IReadOnlyList<(int SubAssemblyId, decimal AuthoredValue)> usages)
    {
        if (usages.Any(u => u.AuthoredValue <= 0))
        {
            throw new InvalidOperationException("A sub-assembly's weight per piece or yield must be positive.");
        }

        if (usages.Select(u => u.SubAssemblyId).Distinct().Count() != usages.Count)
        {
            throw new InvalidOperationException("A product can't use the same sub-assembly more than once.");
        }
    }

    /// <summary>
    /// A usage's weight per piece and yield, from the one the user typed and the batch's current total
    /// weight: the typed one as it is, the other worked out (Yield = TotalWeight / WeightPerPiece). Worked
    /// out on every read, so the typed figure stays put when the batch changes. A batch that weighs
    /// nothing yet (a new sub-assembly with no lines) has no other figure: it comes out as 0.
    /// </summary>
    public static (decimal WeightPerPiece, decimal Yield) ResolvePieceSize(UsageAuthoredField authoredField, decimal authoredValue, decimal totalWeight)
    {
        var other = totalWeight > 0 && authoredValue > 0 ? totalWeight / authoredValue : 0m;
        return authoredField switch
        {
            UsageAuthoredField.WeightPerPiece => (authoredValue, other),
            UsageAuthoredField.Yield => (other, authoredValue),
            _ => throw new ArgumentOutOfRangeException(nameof(authoredField)),
        };
    }

    /// <summary>This usage's share of the sub-assembly's cost, per finished piece.</summary>
    public static decimal CalculateUsageCostPerPiece(decimal subAssemblyTotalCost, decimal weightPerPiece, decimal subAssemblyTotalWeight) =>
        subAssemblyTotalCost * weightPerPiece / subAssemblyTotalWeight;

    /// <summary>
    /// How many grams of one sub-assembly line go into one finished piece: the piece takes
    /// weightPerPiece grams of the whole batch, so it takes that share of every line.
    /// </summary>
    public static decimal CalculateLineGramsPerPiece(decimal lineGrams, decimal weightPerPiece, decimal subAssemblyTotalWeight) =>
        lineGrams * weightPerPiece / subAssemblyTotalWeight;

    /// <summary>
    /// One sub-assembly line's cost per finished piece: the piece's share of the line's grams, at the
    /// ingredient's cost per gram. A usage's lines add up to its CalculateUsageCostPerPiece.
    /// </summary>
    public static decimal CalculateLineCostPerPiece(decimal lineGrams, decimal ingredientCostPerGram, decimal weightPerPiece, decimal subAssemblyTotalWeight) =>
        lineGrams * ingredientCostPerGram * weightPerPiece / subAssemblyTotalWeight;

    /// <summary>
    /// Unlike EnsureValidUsages and the ingredient/sub-assembly line rules, an empty list
    /// is deliberately allowed here — a product may legitimately have no packaging lines
    /// entered yet, whereas a sub-assembly with no ingredient lines or a product with no
    /// sub-assembly usage isn't a real recipe.
    /// </summary>
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

    /// <summary>Derives QtyPerPiece and PiecesPerPackagingUnit — exact reciprocals of each other — from whichever one the user actually typed.</summary>
    public static (decimal QtyPerPiece, decimal PiecesPerPackagingUnit) ReconcileReciprocal(
        PackagingLineAuthoredField authoredField, decimal authoredValue) =>
        authoredField switch
        {
            PackagingLineAuthoredField.QtyPerPiece => (authoredValue, 1m / authoredValue),
            PackagingLineAuthoredField.PiecesPerPackagingUnit => (1m / authoredValue, authoredValue),
            _ => throw new ArgumentOutOfRangeException(nameof(authoredField)),
        };

    /// <summary>
    /// Cost of one packaging line per finished piece: QtyPerPiece, in the line's unit, converted to
    /// the material's base units (<paramref name="baseUnitsPerEntryUnit"/>, null when the line is in
    /// the base unit), at the material's cost per base unit.
    /// </summary>
    public static decimal CalculatePackagingLineCostPerPiece(decimal costPerBaseUnit, decimal qtyPerPiece, decimal? baseUnitsPerEntryUnit) =>
        costPerBaseUnit * UnitConversionRules.ToBaseQuantity(qtyPerPiece, baseUnitsPerEntryUnit);

    /// <summary>A product's total material cost per piece: every sub-assembly usage's cost-per-piece, plus every packaging line's cost-per-piece.</summary>
    public static decimal CalculateMaterialCostPerPiece(IReadOnlyList<decimal> usageCostsPerPiece, IReadOnlyList<decimal> packagingCostsPerPiece) =>
        usageCostsPerPiece.Sum() + packagingCostsPerPiece.Sum();
}
