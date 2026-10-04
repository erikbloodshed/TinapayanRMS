using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// Scaling a product's recipe to a number of sellable units (the Products screen's Scaling tab): how many pieces that
/// is, how many whole batches of each section make them, and how much of each made ingredient's BOM goes into it.
/// </summary>
public static class RecipeScalingRules
{
    /// <summary>The places a batch ratio is rounded to before it's rounded up, as grams are stored: decimal noise, not a batch.</summary>
    private const int RatioDecimals = 4;

    /// <summary>The finished pieces <paramref name="units"/> sellable units hold: 20 boxes of 8 are 160 pieces.</summary>
    public static decimal PiecesFor(decimal units, int quantityRequired) => units * quantityRequired;

    /// <summary>
    /// The whole batches of a section that make at least <paramref name="pieces"/>: rounded up, so there are pieces to
    /// spare rather than too few. Worked out from the figure that was typed (<paramref name="authoredField"/>), not the
    /// one worked out from it: 85 pieces at a typed 60 g from 1,700 g is exactly 3 batches, where the worked-out yield,
    /// 28.3333…, would make it a hair over 3 and so 4. 0 for a section with no piece size yet, or a batch of nothing.
    /// </summary>
    public static decimal BatchesFor(decimal pieces, UsageAuthoredField authoredField, decimal authoredValue, decimal finalWeight) =>
        WholeBatches(BatchRatio(pieces, authoredField, authoredValue, finalWeight));

    /// <summary>
    /// The batches of a section <paramref name="pieces"/> take, not rounded: 160 pieces at 10 g from a 1,000 g batch are 1.6.
    /// The Production Plan adds these up across the products sharing a batch before rounding up once (<see cref="WholeBatches"/>).
    /// 0 for a section with no piece size yet, or a batch of nothing.
    /// </summary>
    public static decimal BatchRatio(decimal pieces, UsageAuthoredField authoredField, decimal authoredValue, decimal finalWeight)
    {
        if (pieces <= 0 || authoredValue <= 0 || finalWeight <= 0)
        {
            return 0m;
        }

        return authoredField switch
        {
            UsageAuthoredField.WeightPerPiece => pieces * authoredValue / finalWeight,
            UsageAuthoredField.Yield => pieces / authoredValue,
            _ => throw new ArgumentOutOfRangeException(nameof(authoredField)),
        };
    }

    /// <summary>
    /// <paramref name="ratio"/> rounded up to whole batches, after rounding off decimal noise (<see cref="RatioDecimals"/>
    /// places), so an exact 3 that comes out a hair over isn't 4. Anything at all needs one: a need too small to survive
    /// the noise rounding is still a need.
    /// </summary>
    public static decimal WholeBatches(decimal ratio) => ratio > 0m ? Math.Max(1m, Math.Ceiling(Math.Round(ratio, RatioDecimals))) : 0m;

    /// <summary>
    /// The whole purchase units to buy for <paramref name="baseQuantity"/>: 30,000 g of flour in 25,000 g sacks is 2 sacks.
    /// Rounded up as batches are, so an exact multiple isn't one more, and 1 g of it is still a sack.
    /// </summary>
    public static decimal UnitsToBuy(decimal baseQuantity, decimal baseUnitsPerUnit) =>
        baseUnitsPerUnit > 0 ? WholeBatches(baseQuantity / baseUnitsPerUnit) : 0m;

    /// <summary>The pieces whole batches make beyond what's needed: 2 batches of 100 for 160 pieces leave 40. Never below 0.</summary>
    public static decimal ExtraPieces(decimal batches, decimal yield, decimal pieces) =>
        Math.Max(0m, batches * yield - pieces);

    /// <summary>
    /// A made ingredient's BOM lines, scaled so they make <paramref name="requiredGrams"/> once its weight loss is taken:
    /// 1,200 g of a syrup made from 1,000 g of sugar and 500 g of water at 20% loss takes all 1,500 g. Exact, not rounded
    /// to whole batches. Lines that make nothing (no weight, or all of it lost) scale to 0.
    /// </summary>
    public static IReadOnlyList<decimal> ScaleBom(decimal requiredGrams, IReadOnlyList<decimal> lineGrams, decimal weightLossPercent)
    {
        var outputGrams = IngredientBomRules.CalculateOutputGrams(lineGrams.Sum(), weightLossPercent);
        return outputGrams > 0
            ? [.. lineGrams.Select(g => g * requiredGrams / outputGrams)]
            : [.. lineGrams.Select(_ => 0m)];
    }
}
