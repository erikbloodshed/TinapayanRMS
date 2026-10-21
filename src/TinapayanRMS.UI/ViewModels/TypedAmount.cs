using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// An amount typed into a costing metric's value box (Products' Costing tab), with the percentage it set and the cost
/// it's a percentage of. The percentage is what's kept; the amount is worked out from it. Worked out again, it can come
/// back a hair off what was typed (23.77 as 23.7699…), and the box would rewrite itself under the cursor. So while the
/// percentage and its cost are still the ones typed against, the box is shown what was typed.
/// </summary>
public sealed record TypedAmount(decimal Amount, decimal Percent, decimal OfCost)
{
    /// <summary>The costing columns' scale (numeric(18,4)): a percentage typed as an amount is kept as it will be saved.</summary>
    private const int PercentDecimals = 4;

    /// <summary>What the value box shows: the amount typed, while it still stands, or else the one worked out.</summary>
    public static decimal Shown(TypedAmount? typed, decimal percent, decimal ofCost, decimal workedOut) =>
        typed is not null && typed.Percent == percent && typed.OfCost == ofCost ? typed.Amount : workedOut;

    /// <summary>
    /// The percentage of <paramref name="ofCost"/> that <paramref name="amount"/> comes to, remembering the amount in
    /// <paramref name="typed"/>. With nothing to take a percentage of (no recipe yet) there's none, so the percentage stays
    /// as it was and the box goes back to what it comes to.
    /// </summary>
    public static decimal PercentFor(ref TypedAmount? typed, decimal amount, decimal ofCost, decimal currentPercent)
    {
        if (ofCost == 0)
        {
            typed = null;
            return currentPercent;
        }

        var percent = Math.Round(CostingRules.PercentOf(amount, ofCost), PercentDecimals);
        typed = new TypedAmount(amount, percent, ofCost);
        return percent;
    }
}
