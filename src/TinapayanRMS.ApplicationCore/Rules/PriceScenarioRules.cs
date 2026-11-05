using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// A Price Simulator scenario: the price changes in its table, saved under a name to open again later.
/// Nothing in one changes a real price.
/// </summary>
public static class PriceScenarioRules
{
    public const int NameMaxLength = 100;

    public const string NameRequiredText = "A scenario needs a name.";

    public static readonly string NameTooLongText = $"A scenario's name can be at most {NameMaxLength} characters.";

    public const string NoChangesText = "There are no price changes to save. Pick a raw material and type its new price or change first.";

    public const string RepeatedMaterialText = "A raw material can only be in a scenario once.";

    public const string OneTypedText = "Each price change needs either its change in percent or its new price.";

    /// <summary>
    /// Refuses a scenario that can't be saved: no name or too long a name, no lines, a material twice, or a line without
    /// exactly one of its change in percent and its new price, or with one that can't be (PriceChangeRules).
    /// </summary>
    public static void EnsureValid(string name, IReadOnlyList<PriceScenarioLine> lines)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessRuleException(NameRequiredText);
        }

        if (name.Trim().Length > NameMaxLength)
        {
            throw new BusinessRuleException(NameTooLongText);
        }

        if (lines.Count == 0)
        {
            throw new BusinessRuleException(NoChangesText);
        }

        if (lines.GroupBy(l => l.RawMaterialId).Any(g => g.Count() > 1))
        {
            throw new BusinessRuleException(RepeatedMaterialText);
        }

        foreach (var line in lines)
        {
            switch (line.ChangePercent, line.NewPrice)
            {
                case ({ } percent, null):
                    PriceChangeRules.EnsureValidChange(percent);
                    break;
                case (null, { } price):
                    PriceChangeRules.EnsureValidNewPrice(price);
                    break;
                default:
                    throw new BusinessRuleException(OneTypedText);
            }
        }
    }
}
