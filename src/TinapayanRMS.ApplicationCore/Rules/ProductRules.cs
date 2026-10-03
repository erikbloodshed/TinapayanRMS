using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Rules;

public static class ProductRules
{
    /// <summary>
    /// A product's whole set of usages: an empty set is valid (PLAN.md doesn't require a product to
    /// have a usage at all times), and so is a piece size of 0 — a section can go into the recipe before
    /// anyone knows how much of it a piece takes (2026-09-20, at the user's request), and until it's
    /// known the section costs the product nothing per piece (ResolvePieceSize, CalculateUsageCostPerPiece).
    /// A negative piece size is still nonsense, shrinkage runs from 0 to under 100 (a batch can't lose all
    /// of itself, or there'd be nothing left to cut pieces from), and a product uses each sub-assembly once.
    /// </summary>
    public static void EnsureValidUsages(IReadOnlyList<(int SubAssemblyId, decimal AuthoredValue, decimal ShrinkagePercent)> usages)
    {
        if (usages.Any(u => u.AuthoredValue < 0))
        {
            throw new BusinessRuleException("A sub-assembly's weight per piece or yield can't be negative.");
        }

        if (usages.Any(u => u.ShrinkagePercent < 0 || u.ShrinkagePercent >= 100))
        {
            throw new BusinessRuleException("A sub-assembly's shrinkage must be at least 0% and less than 100%.");
        }

        if (usages.Select(u => u.SubAssemblyId).Distinct().Count() != usages.Count)
        {
            throw new BusinessRuleException("A product can't use the same sub-assembly more than once.");
        }
    }

    /// <summary>
    /// A usage's add-ins: this product's own extra ingredients on top of a recipe it links — a colouring,
    /// a flavouring, an inclusion. Unlike a sub-assembly's own lines, an empty set is valid: most linked
    /// usages take the recipe as it is. What isn't valid is an ingredient the recipe already has, since
    /// add-ins are only for what it doesn't. <paramref name="baseIngredientIds"/> are the linked
    /// sub-assembly's own lines' ingredients.
    /// </summary>
    public static void EnsureValidAddIns(
        IReadOnlyList<(int IngredientId, decimal Grams, string IngredientName)> addIns,
        string subAssemblyName,
        IReadOnlyCollection<int> baseIngredientIds)
    {
        if (addIns.Any(a => a.Grams <= 0))
        {
            throw new BusinessRuleException("An add-in's grams must be positive.");
        }

        var duplicate = addIns.GroupBy(a => a.IngredientId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new BusinessRuleException(
                $"{duplicate.First().IngredientName} is on two add-in lines. Put its total on one line and delete the other.");
        }

        var alreadyThere = addIns.Where(a => baseIngredientIds.Contains(a.IngredientId)).Select(a => a.IngredientName).FirstOrDefault();
        if (alreadyThere is not null)
        {
            throw new BusinessRuleException(
                $"{alreadyThere} is already in {subAssemblyName}. Add-ins are only for what its recipe doesn't have — "
                + $"change how much it uses where {subAssemblyName} is kept, or make this product a copy of it.");
        }
    }

    /// <summary>
    /// A usage's effective batch: the sub-assembly's own, plus this product's add-ins. Add-ins go into the
    /// batch, so 1,700 g of dough with 20 g of flavouring is a 1,720 g batch and a piece takes its
    /// weight-per-piece share of the whole thing. Pass the sub-assembly's stored TotalWeight and TotalCost,
    /// or the same figures worked out from its lines — they're the same figures. A sub-assembly of the
    /// product's own never has add-ins, so its effective batch is its own.
    /// </summary>
    public static (decimal TotalWeight, decimal TotalCost) CalculateEffectiveBatch(
        decimal subAssemblyTotalWeight,
        decimal subAssemblyTotalCost,
        IReadOnlyList<(decimal Grams, decimal IngredientCostPerGram)> addIns)
    {
        var (addInWeight, addInCost) = SubAssemblyRules.CalculateTotals(addIns);
        return (subAssemblyTotalWeight + addInWeight, subAssemblyTotalCost + addInCost);
    }

    /// <summary>
    /// What a usage's batch weighs once it's made: what went in, less the share of it this product loses
    /// baking or cooking it off (<paramref name="shrinkagePercent"/>, at least 0 and under 100 —
    /// EnsureValidUsages). A 1,700 g batch at 10% comes out at 1,530 g. Pieces are cut from this, not from
    /// the raw batch, so it's what ResolvePieceSize and CalculateUsageCostPerPiece divide; the batch's cost
    /// doesn't change with it, so a batch that loses weight makes fewer, dearer pieces.
    /// RawMaterial.WeightLossPercent is the same idea one layer down (IngredientBomRules.CalculateCost).
    /// </summary>
    public static decimal CalculateFinalWeight(decimal totalWeight, decimal shrinkagePercent) =>
        totalWeight * (1 - shrinkagePercent / 100m);

    /// <summary>
    /// A usage's weight per piece and yield, from the one the user typed and what the batch weighs once
    /// it's made (<paramref name="finalWeight"/>, from CalculateFinalWeight): the typed one as it is, the
    /// other worked out (Yield = FinalWeight / WeightPerPiece). Both figures are the finished piece's, and
    /// both are worked out on every read, so the typed one stays put when the batch or the shrinkage
    /// changes. A batch that weighs nothing yet (a new sub-assembly with no lines) has no other figure: it
    /// comes out as 0.
    /// </summary>
    public static (decimal WeightPerPiece, decimal Yield) ResolvePieceSize(UsageAuthoredField authoredField, decimal authoredValue, decimal finalWeight)
    {
        var other = finalWeight > 0 && authoredValue > 0 ? finalWeight / authoredValue : 0m;
        return authoredField switch
        {
            UsageAuthoredField.WeightPerPiece => (authoredValue, other),
            UsageAuthoredField.Yield => (other, authoredValue),
            _ => throw new ArgumentOutOfRangeException(nameof(authoredField)),
        };
    }

    /// <summary>
    /// This usage's share of the sub-assembly's cost, per finished piece. The batch's whole cost is spread
    /// over what comes out of it (<paramref name="finalWeight"/>), so this comes to the same thing as
    /// TotalCost / Yield.
    /// </summary>
    public static decimal CalculateUsageCostPerPiece(decimal subAssemblyTotalCost, decimal weightPerPiece, decimal finalWeight) =>
        subAssemblyTotalCost * weightPerPiece / finalWeight;

    /// <summary>
    /// How many grams of one sub-assembly line go into one finished piece: the piece is weightPerPiece of
    /// what the batch makes, so it takes that share of every line that went in. Pass the final weight, as
    /// CalculateUsageCostPerPiece takes it.
    /// </summary>
    public static decimal CalculateLineGramsPerPiece(decimal lineGrams, decimal weightPerPiece, decimal finalWeight) =>
        lineGrams * weightPerPiece / finalWeight;

    /// <summary>
    /// One sub-assembly line's cost per finished piece: the piece's share of the line's grams, at the
    /// ingredient's cost per gram. A usage's lines add up to its CalculateUsageCostPerPiece.
    /// </summary>
    public static decimal CalculateLineCostPerPiece(decimal lineGrams, decimal ingredientCostPerGram, decimal weightPerPiece, decimal finalWeight) =>
        lineGrams * ingredientCostPerGram * weightPerPiece / finalWeight;

    /// <summary>
    /// A product's packaging lines. Unlike EnsureValidUsages and the ingredient/sub-assembly line rules,
    /// an empty list is deliberately allowed here — a product may legitimately have no packaging lines
    /// entered yet, whereas a sub-assembly with no ingredient lines or a product with no sub-assembly
    /// usage isn't a real recipe.
    /// </summary>
    public static void EnsureValidPackagingLines(IReadOnlyList<(int PackagingMaterialId, decimal QtyPerUnit)> lines)
    {
        if (lines.Any(l => l.QtyPerUnit <= 0))
        {
            throw new BusinessRuleException("A packaging line's quantity must be positive.");
        }

        if (lines.GroupBy(l => l.PackagingMaterialId).Any(g => g.Count() > 1))
        {
            throw new BusinessRuleException(
                "A product can't use the same packaging material more than once — combine it into a single line instead.");
        }
    }

    /// <summary>Derives QtyPerUnit and UnitsPerPackagingUnit — exact reciprocals of each other — from whichever one the user actually typed.</summary>
    public static (decimal QtyPerUnit, decimal UnitsPerPackagingUnit) ReconcileReciprocal(
        PackagingLineAuthoredField authoredField, decimal authoredValue) =>
        authoredField switch
        {
            PackagingLineAuthoredField.QtyPerUnit => (authoredValue, 1m / authoredValue),
            PackagingLineAuthoredField.UnitsPerPackagingUnit => (1m / authoredValue, authoredValue),
            _ => throw new ArgumentOutOfRangeException(nameof(authoredField)),
        };

    /// <summary>
    /// Cost of one packaging line per sellable unit of the product: QtyPerUnit, in the line's unit,
    /// converted to the material's base units (<paramref name="baseUnitsPerEntryUnit"/>, null when the
    /// line is in the base unit), at the material's cost per base unit.
    /// </summary>
    public static decimal CalculatePackagingLineCost(decimal costPerBaseUnit, decimal qtyPerUnit, decimal? baseUnitsPerEntryUnit) =>
        costPerBaseUnit * UnitConversionRules.ToBaseQuantity(qtyPerUnit, baseUnitsPerEntryUnit);

    /// <summary>
    /// A product's total material cost, per sellable unit: the pieces that unit holds
    /// (<paramref name="quantityRequired"/>) at every sub-assembly usage's cost-per-piece, plus the
    /// packaging that wraps the whole unit. The recipe is authored per piece whatever the quantity is,
    /// so only the usages are multiplied; the packaging lines are already for one whole unit.
    /// </summary>
    public static decimal CalculateMaterialCost(
        IReadOnlyList<decimal> usageCostsPerPiece, int quantityRequired, IReadOnlyList<decimal> packagingCosts) =>
        usageCostsPerPiece.Sum() * quantityRequired + packagingCosts.Sum();
}
