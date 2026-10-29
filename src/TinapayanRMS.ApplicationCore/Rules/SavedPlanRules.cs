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

    public const string MadeText = "What was made can't be below 0.";

    public const string LeftoverText = "What was left over must be from 0 to what was made, and needs Made typed first.";

    /// <summary>
    /// Refuses a plan that can't be saved: no name or too long a name, no lines, a line of 0 units or fewer, or a line's
    /// results that can't be (2026-10-29): made below 0, or left over below 0, over what was made, or without it.
    /// </summary>
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

        if (lines.Any(l => l.MadeUnits < 0m))
        {
            throw new BusinessRuleException(MadeText);
        }

        if (lines.Any(l => l.LeftoverUnits is { } left && (left < 0m || l.MadeUnits is not { } made || left > made)))
        {
            throw new BusinessRuleException(LeftoverText);
        }
    }
}
