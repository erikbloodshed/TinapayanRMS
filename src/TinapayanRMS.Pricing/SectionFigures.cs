using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing;

/// <summary>
/// A recipe section's batch and piece figures, worked out once from its lines, its add-ins and its piece size, at the
/// ingredients' buffered costs and at their true ones, with the whole batches of any of the product's sections that go into
/// it. The Products screen's sections and a saved product's both get theirs from <c>Of</c>, through <see cref="SectionFolds"/>
/// when one goes into another.
/// </summary>
/// <param name="BaseTotalWeight">The sub-assembly's own batch, before the product's add-ins.</param>
/// <param name="TotalWeight">
/// The batch a piece is cut from: the sub-assembly's own plus the add-ins, and the whole batches of the product's sections
/// that go into it (<see cref="FoldedInWeight"/>).
/// </param>
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
    /// <summary>
    /// What goes into the batch whole from the product's other sections (a sponge into the dough): their whole batches,
    /// counted in <see cref="TotalWeight"/> and the total costs. Nothing for a section nothing goes into.
    /// </summary>
    public decimal FoldedInWeight { get; init; }

    /// <summary>
    /// The section goes whole into another of the product's (SectionFolds): its batch is part of that one's, so it has no
    /// pieces of its own and costs the product nothing per piece by itself.
    /// </summary>
    public bool GoesWhole { get; init; }

    /// <summary>What the whole batch costs, add-ins and anything folded in included, at <paramref name="basis"/>.</summary>
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
        decimal authoredValue) =>
        Of(lines, addIns, authoredField, authoredValue, FoldedBatch.None, goesWhole: false);

    /// <summary>
    /// The same, with <paramref name="foldedIn"/>, the whole batches of the sections that go into it, in its batch too, and
    /// when <paramref name="goesWhole"/>, as a section that goes whole into another: no pieces of its own, whatever piece
    /// size it was given.
    /// </summary>
    public static SectionFigures Of(
        IReadOnlyList<SectionLine> lines,
        IReadOnlyList<SectionLine> addIns,
        UsageAuthoredField authoredField,
        decimal authoredValue,
        FoldedBatch foldedIn,
        bool goesWhole)
    {
        var (baseTotalWeight, baseBufferedCost) = SubAssemblyRules.CalculateTotals(CostInputs(lines, CostBasis.Buffered));
        var baseUnbufferedCost = SubAssemblyRules.CalculateTotals(CostInputs(lines, CostBasis.Unbuffered)).TotalCost;
        var (effectiveWeight, effectiveBufferedCost) = ProductRules.CalculateEffectiveBatch(baseTotalWeight, baseBufferedCost, CostInputs(addIns, CostBasis.Buffered));
        var effectiveUnbufferedCost = ProductRules.CalculateEffectiveBatch(baseTotalWeight, baseUnbufferedCost, CostInputs(addIns, CostBasis.Unbuffered)).TotalCost;
        var totalWeight = effectiveWeight + foldedIn.Weight;
        var bufferedTotalCost = effectiveBufferedCost + foldedIn.BufferedCost;
        var unbufferedTotalCost = effectiveUnbufferedCost + foldedIn.UnbufferedCost;
        var (weightPerPiece, yield) = goesWhole ? (0m, 0m) : ProductRules.ResolvePieceSize(authoredField, authoredValue, totalWeight);

        // A batch that weighs nothing, or has no piece size yet, has nothing to share out per piece: the rule gives 0.
        return new SectionFigures(
            baseTotalWeight,
            totalWeight,
            weightPerPiece,
            yield,
            bufferedTotalCost,
            unbufferedTotalCost,
            ProductRules.CalculateUsageCostPerPiece(bufferedTotalCost, weightPerPiece, totalWeight),
            ProductRules.CalculateUsageCostPerPiece(unbufferedTotalCost, weightPerPiece, totalWeight))
        {
            FoldedInWeight = foldedIn.Weight,
            GoesWhole = goesWhole,
        };
    }

    /// <summary>This section's whole batch, as it goes into another.</summary>
    public FoldedBatch AsFolded => new(TotalWeight, BufferedTotalCost, UnbufferedTotalCost);

    private static List<(decimal Grams, decimal IngredientCostPerGram)> CostInputs(IReadOnlyList<SectionLine> lines, CostBasis basis) =>
        [.. lines.Select(l => (l.Grams, l.CostPerGramAt(basis)))];
}

/// <summary>
/// The whole batches of a product's sections that go into another (SectionFolds), together: their weight, and their cost at
/// the materials' buffered costs and at their true ones.
/// </summary>
public readonly record struct FoldedBatch(decimal Weight, decimal BufferedCost, decimal UnbufferedCost)
{
    public static FoldedBatch None => default;

    public static FoldedBatch operator +(FoldedBatch a, FoldedBatch b) =>
        new(a.Weight + b.Weight, a.BufferedCost + b.BufferedCost, a.UnbufferedCost + b.UnbufferedCost);

    /// <summary>What it costs at <paramref name="basis"/>.</summary>
    public decimal CostAt(CostBasis basis) => basis == CostBasis.Unbuffered ? UnbufferedCost : BufferedCost;
}
