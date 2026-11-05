using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing;

/// <summary>
/// A recipe section's batch and piece figures, worked out once from its lines, its add-ins and its piece size, at the
/// ingredients' buffered costs and at their true ones. The Products screen's sections and a saved product's both get
/// theirs from <see cref="Of"/>.
/// </summary>
/// <param name="BaseTotalWeight">The sub-assembly's own batch, before the product's add-ins.</param>
/// <param name="TotalWeight">The batch a piece is cut from: the sub-assembly's own plus the add-ins.</param>
/// <param name="WeightPerPiece">Grams of the batch in one piece.</param>
/// <param name="Yield">Pieces one batch makes.</param>
public sealed record SectionFigures(
    decimal BaseTotalWeight,
    decimal TotalWeight,
    decimal WeightPerPiece,
    decimal Yield,
    decimal BufferedTotalCost,
    decimal UnbufferedTotalCost,
    decimal BufferedCostPerPiece,
    decimal UnbufferedCostPerPiece)
{
    /// <summary>What the whole batch costs, add-ins included, at <paramref name="basis"/>.</summary>
    public decimal TotalCostAt(CostBasis basis) => basis == CostBasis.Unbuffered ? UnbufferedTotalCost : BufferedTotalCost;

    /// <summary>What one piece costs at <paramref name="basis"/>; 0 while the batch weighs nothing or has no piece size.</summary>
    public decimal CostPerPieceAt(CostBasis basis) => basis == CostBasis.Unbuffered ? UnbufferedCostPerPiece : BufferedCostPerPiece;

    /// <summary>
    /// The figures of a section of <paramref name="lines"/> with <paramref name="addIns"/> on top, cut into pieces by the
    /// figure typed (<paramref name="authoredField"/>). Add-ins go into the batch, so a piece takes its share of both.
    /// </summary>
    public static SectionFigures Of(
        IReadOnlyList<SectionLine> lines,
        IReadOnlyList<SectionLine> addIns,
        UsageAuthoredField authoredField,
        decimal authoredValue)
    {
        var (baseTotalWeight, baseBufferedCost) = SubAssemblyRules.CalculateTotals(CostInputs(lines, CostBasis.Buffered));
        var baseUnbufferedCost = SubAssemblyRules.CalculateTotals(CostInputs(lines, CostBasis.Unbuffered)).TotalCost;
        var (totalWeight, bufferedTotalCost) = ProductRules.CalculateEffectiveBatch(baseTotalWeight, baseBufferedCost, CostInputs(addIns, CostBasis.Buffered));
        var unbufferedTotalCost = ProductRules.CalculateEffectiveBatch(baseTotalWeight, baseUnbufferedCost, CostInputs(addIns, CostBasis.Unbuffered)).TotalCost;
        var (weightPerPiece, yield) = ProductRules.ResolvePieceSize(authoredField, authoredValue, totalWeight);

        // A batch that weighs nothing, or has no piece size yet, has nothing to share out per piece.
        var hasPieces = totalWeight > 0 && weightPerPiece > 0;
        return new SectionFigures(
            baseTotalWeight,
            totalWeight,
            weightPerPiece,
            yield,
            bufferedTotalCost,
            unbufferedTotalCost,
            hasPieces ? ProductRules.CalculateUsageCostPerPiece(bufferedTotalCost, weightPerPiece, totalWeight) : 0m,
            hasPieces ? ProductRules.CalculateUsageCostPerPiece(unbufferedTotalCost, weightPerPiece, totalWeight) : 0m);
    }

    private static List<(decimal Grams, decimal IngredientCostPerGram)> CostInputs(IReadOnlyList<SectionLine> lines, CostBasis basis) =>
        [.. lines.Select(l => (l.Grams, l.CostPerGramAt(basis)))];
}
