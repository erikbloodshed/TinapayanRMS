using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// A saved Production Plan (2026-10-24): the products in the plan's table and how many, saved under a name to open again.
/// </summary>
public static class SavedPlanRules
{
    public const int NameMaxLength = 100;

    public const string NameRequiredText = "A plan needs a name.";

    public static readonly string NameTooLongText = $"A plan's name can be at most {NameMaxLength} characters.";

    public const string NoLinesText = "There's nothing in the plan to save. Pick a product and type how many to make first.";

    public const string UnitsText = "Each product in a plan needs more than 0 units to make.";

    /// <summary>Refuses a plan that can't be saved: no name or too long a name, no lines, or a line of 0 units or fewer.</summary>
    public static void EnsureValid(string name, IReadOnlyList<SavedPlanLine> lines)
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
            throw new BusinessRuleException(NoLinesText);
        }

        if (lines.Any(l => l.Units <= 0m))
        {
            throw new BusinessRuleException(UnitsText);
        }
    }
}
