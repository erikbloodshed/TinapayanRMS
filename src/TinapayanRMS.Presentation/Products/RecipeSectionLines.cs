using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// What the section form asks of a recipe's sections as lines of each other (ProductUsageEdit.SectionLines): which a
/// section's lines can use, one of them as a line prices it, what the others use of one, and whether one can be taken out.
/// Read from the recipe as it's priced now (ProductRecipeDraft.Priced).
/// </summary>
internal static class RecipeSectionLines
{
    /// <summary>
    /// <paramref name="draft"/>'s section keyed <paramref name="key"/> as a line of another can use it, at its cost per gram
    /// in the recipe; null for none.
    /// </summary>
    public static LineSection? Find(ProductRecipeDraft draft, long key) =>
        SectionFormSnapshot.IndexOfKey(draft.UsageEdits, key) is { } i and >= 0 ? At(draft, i) : null;

    private static LineSection At(ProductRecipeDraft draft, int index)
    {
        var figures = draft.Priced()[index].Figures;
        var section = draft.UsageEdits[index];
        return new LineSection(section.Key, section.SectionName, figures.CostPerGramAt(CostBasis.Buffered), figures.CostPerGramAt(CostBasis.Unbuffered));
    }

    /// <summary>
    /// The sections <paramref name="usage"/>'s lines can use: the recipe's others in a role that can go into another
    /// (SubAssemblyRole.CanGoIntoOtherSections, as the screen last loaded the roles), less any that uses it already, even
    /// through others, which would make a loop (ProductRules.EnsureValidSectionLines). For a section being added, every one
    /// in such a role. One already on its lines, or on a row of <paramref name="onRows"/> (the form's, typed but not yet
    /// applied), stays whatever its role, so the row still shows it, and Save says what's wrong. In recipe order.
    /// </summary>
    public static IReadOnlyList<LineSection> For(ProductRecipeDraft draft, ProductUsageEdit? usage, IEnumerable<long>? onRows = null)
    {
        var usesOf = draft.UsageEdits.ToDictionary(u => u.Key, u => u.SectionLines.Select(l => l.Key));
        HashSet<long> kept = [.. usage?.SectionLines.Select(l => l.Key) ?? [], .. onRows ?? []];
        HashSet<int> intoOthers = [.. draft.Roles.Where(r => r.CanGoIntoOtherSections).Select(r => r.Id)];
        return
        [
            .. draft.UsageEdits
                .Select((section, i) => (section, i))
                .Where(p => (intoOthers.Contains(p.section.RoleId) || kept.Contains(p.section.Key))
                    && (usage is null
                        || (p.section.Key != usage.Key && !ProductRules.Reaches(p.section.Key, usage.Key, key => usesOf.GetValueOrDefault(key, [])))))
                .Select(p => At(draft, p.i)),
        ];
    }

    /// <summary>
    /// Refuses to take <paramref name="usage"/> out of the recipe while another section's lines use it (Delete Sub-Assembly):
    /// that one's batch would change unseen.
    /// </summary>
    public static void EnsureNotUsedElsewhere(ProductRecipeDraft draft, ProductUsageEdit usage)
    {
        if (UsedElsewhere(draft, usage).Users is { Count: > 0 } users)
        {
            throw new BusinessRuleException(
                $"{string.Join(" and ", users)} {(users.Count == 1 ? "uses" : "use")} {usage.SectionName}. Take it off {(users.Count == 1 ? "its" : "their")} lines first.");
        }
    }

    /// <summary>What the recipe's other sections use of <paramref name="usage"/> on their lines, and their names: what its pieces come of what's left of.</summary>
    public static (decimal Grams, IReadOnlyList<string> Users) UsedElsewhere(ProductRecipeDraft draft, ProductUsageEdit usage) =>
        (draft.FiguresOf(usage).UsedElsewhereWeight,
            [.. draft.UsageEdits.Where(u => !ReferenceEquals(u, usage) && u.SectionLines.Any(l => l.Key == usage.Key)).Select(u => u.SectionName)]);
}
