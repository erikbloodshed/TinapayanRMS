using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing;

/// <summary>
/// A recipe section's batch and piece figures, worked out once from its lines, its add-ins and its piece size, at the
/// ingredients' buffered costs and at their true ones, with its lines that are others of the product's sections. The
/// Products screen's sections and a saved product's both get theirs from <c>Of</c>, through <see cref="SectionUses"/> when one
/// is on another's lines.
/// </summary>
/// <param name="BaseTotalWeight">The sub-assembly's own batch, before the product's add-ins.</param>
/// <param name="TotalWeight">
/// The whole batch: the sub-assembly's own plus the add-ins, and its lines that are the product's other sections
/// (<see cref="SectionLinesWeight"/>).
/// </param>
/// <param name="WeightPerPiece">Grams of the batch in one piece.</param>
/// <param name="Yield">Pieces the batch makes, of what the product's other sections leave of it (<see cref="UsedElsewhereWeight"/>).</param>
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
    /// What its lines that are the product's other sections weigh (a sponge on the dough's lines), counted in
    /// <see cref="TotalWeight"/> and the total costs. Nothing for a section of ingredients alone.
    /// </summary>
    public decimal SectionLinesWeight { get; init; }

    /// <summary>
    /// What the product's other sections use of the batch, on their lines: its pieces come of what's left, so a yield shares
    /// out only that, and the product counts each gram once. Nothing for a section on no other's lines.
    /// </summary>
    public decimal UsedElsewhereWeight { get; init; }

    /// <summary>What the whole batch costs, add-ins and its lines that are sections included, at <paramref name="basis"/>.</summary>
    public decimal TotalCostAt(CostBasis basis) => basis == CostBasis.Unbuffered ? UnbufferedTotalCost : BufferedTotalCost;

    /// <summary>What one piece costs at <paramref name="basis"/>; 0 while the batch weighs nothing or has no piece size.</summary>
    public decimal CostPerPieceAt(CostBasis basis) => basis == CostBasis.Unbuffered ? UnbufferedCostPerPiece : BufferedCostPerPiece;

    /// <summary>What a gram of the batch costs at <paramref name="basis"/>: what another section's line of it costs a gram; 0 while it weighs nothing.</summary>
    public decimal CostPerGramAt(CostBasis basis) => TotalWeight > 0 ? TotalCostAt(basis) / TotalWeight : 0m;

    /// <summary>
    /// The figures of a section of <paramref name="lines"/> with <paramref name="addIns"/> on top, cut into pieces by the
    /// figure typed (<paramref name="authoredField"/>). Add-ins go into the batch, so a piece takes its share of both.
    /// </summary>
    public static SectionFigures Of(
        IReadOnlyList<SectionLine> lines,
        IReadOnlyList<SectionLine> addIns,
        UsageAuthoredField authoredField,
        decimal authoredValue) =>
        Of(lines, addIns, authoredField, authoredValue, SectionsBatch.None, usedElsewhere: 0m);

    /// <summary>
    /// The same, with <paramref name="sectionLines"/>, its lines that are the product's other sections, in its batch too, and
    /// <paramref name="usedElsewhere"/> grams of it on theirs: its piece size shares out what's left of it.
    /// </summary>
    public static SectionFigures Of(
        IReadOnlyList<SectionLine> lines,
        IReadOnlyList<SectionLine> addIns,
        UsageAuthoredField authoredField,
        decimal authoredValue,
        SectionsBatch sectionLines,
        decimal usedElsewhere)
    {
        var (baseTotalWeight, baseBufferedCost) = SubAssemblyRules.CalculateTotals(CostInputs(lines, CostBasis.Buffered));
        var baseUnbufferedCost = SubAssemblyRules.CalculateTotals(CostInputs(lines, CostBasis.Unbuffered)).TotalCost;
        var (effectiveWeight, effectiveBufferedCost) = ProductRules.CalculateEffectiveBatch(baseTotalWeight, baseBufferedCost, CostInputs(addIns, CostBasis.Buffered));
        var effectiveUnbufferedCost = ProductRules.CalculateEffectiveBatch(baseTotalWeight, baseUnbufferedCost, CostInputs(addIns, CostBasis.Unbuffered)).TotalCost;
        var totalWeight = effectiveWeight + sectionLines.Weight;
        var bufferedTotalCost = effectiveBufferedCost + sectionLines.BufferedCost;
        var unbufferedTotalCost = effectiveUnbufferedCost + sectionLines.UnbufferedCost;
        var (weightPerPiece, yield) = PieceSizeOfWhatsLeft(authoredField, authoredValue, totalWeight, usedElsewhere);

        // A batch that weighs nothing, or has no piece size yet, has nothing to share out per piece: the rule gives 0. A piece
        // costs its weight at the batch's cost per gram, whatever of the batch the others use.
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
            SectionLinesWeight = sectionLines.Weight,
            UsedElsewhereWeight = usedElsewhere,
        };
    }

    /// <summary>
    /// The piece size of what the product's other sections leave of a batch of <paramref name="totalWeight"/> grams, when they
    /// use <paramref name="usedElsewhere"/> of it: none at all when they leave nothing, whatever was typed, so the product
    /// never counts the same grams in their batches and in pieces of this one (PricedSection.LacksPieceSize flags it).
    /// </summary>
    public static (decimal WeightPerPiece, decimal Yield) PieceSizeOfWhatsLeft(
        UsageAuthoredField authoredField, decimal authoredValue, decimal totalWeight, decimal usedElsewhere) =>
        usedElsewhere > 0 && totalWeight <= usedElsewhere
            ? (0m, 0m)
            : ProductRules.ResolvePieceSize(authoredField, authoredValue, Math.Max(totalWeight - usedElsewhere, 0m));

    private static List<(decimal Grams, decimal IngredientCostPerGram)> CostInputs(IReadOnlyList<SectionLine> lines, CostBasis basis) =>
        [.. lines.Select(l => (l.Grams, l.CostPerGramAt(basis)))];
}

/// <summary>
/// A section's lines that are the product's other sections (SectionUses), together: their weight, and their cost at the
/// materials' buffered costs and at their true ones.
/// </summary>
public readonly record struct SectionsBatch(decimal Weight, decimal BufferedCost, decimal UnbufferedCost)
{
    public static SectionsBatch None => default;

    public static SectionsBatch operator +(SectionsBatch a, SectionsBatch b) =>
        new(a.Weight + b.Weight, a.BufferedCost + b.BufferedCost, a.UnbufferedCost + b.UnbufferedCost);

    /// <summary>What it costs at <paramref name="basis"/>.</summary>
    public decimal CostAt(CostBasis basis) => basis == CostBasis.Unbuffered ? UnbufferedCost : BufferedCost;
}
