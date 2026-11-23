using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;
using TinapayanRMS.UseCases.Products;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// A version's sections as History's Restore puts them into the form (ProductVersionHistory), as the recipe stands now.
/// A linked one is linked again as it is now, since Save never writes a version's lines into a sub-assembly others may
/// share; one the product owns now, its role made product-owned since, is the product's own, with its lines as they are
/// now, as a load turns it round (ProductRecipeDraft.RefreshRows); one of the product's own keeps its sub-assembly while
/// the product still owns it; and one deleted since comes back as a new section with the version's lines.
/// </summary>
internal static class VersionSections
{
    /// <summary>
    /// The sections, in the version's order and keyed as it had them, with the linked ones changed on Sub-Assemblies since
    /// and the ones the product owns now, by name, for the screen's note.
    /// </summary>
    internal sealed record Restored(IReadOnlyList<ProductUsageEdit> Sections, IReadOnlyList<string> ChangedSinceLinks, IReadOnlyList<string> OwnNow);

    /// <summary>Whether <paramref name="section"/> links a sub-assembly that's still there, so it's put back from that, as it is now.</summary>
    internal static bool IsRelinked(VersionSection section, LinkedSections linkedNow) =>
        !section.IsOwn && linkedNow.SubAssemblies.Any(s => s.Id == section.SubAssemblyId);

    /// <param name="productId">The product the version is put back into.</param>
    /// <param name="linkedNow">What the version linked, as it is now: the sub-assemblies still there, and their lines.</param>
    /// <param name="ownedNow">The sub-assemblies the product owned as the form took it.</param>
    /// <param name="sectionLabelOf">What a linked section goes by beside its role, from its sub-assembly's name.</param>
    internal static Restored Build(
        ProductVersionContent content,
        int productId,
        LinkedSections linkedNow,
        IReadOnlyDictionary<int, RawMaterial> ingredientsById,
        IReadOnlyList<SubAssemblyRole> roles,
        IReadOnlySet<int> ownedNow,
        Func<string, SubAssemblyRole?, string?> sectionLabelOf)
    {
        var subAssembliesNow = linkedNow.SubAssemblies.ToDictionary(s => s.Id);
        var linesNow = linkedNow.Lines.ToLookup(l => l.SubAssemblyId);
        List<string> changedSinceLinks = [];
        List<string> ownNow = [];
        List<ProductUsageEdit> sections = [];
        foreach (var section in content.Sections)
        {
            if (IsRelinked(section, linkedNow))
            {
                var linked = subAssembliesNow[section.SubAssemblyId];
                var current = linesNow[linked.Id].ToList();
                var linkedRole = roles.FirstOrDefault(r => r.Id == linked.RoleId) ?? linked.Role;
                var lines = current.Select(l => new SectionLine(
                    ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent));

                if (linked.OwnerProductId == productId)
                {
                    // Its role was made product-owned since: the product owns it now, any add-ins it had merged into its
                    // lines then. Linked again, its first Save was refused as one the product took after it was opened.
                    ownNow.Add(linked.Name);
                    sections.Add(new ProductUsageEdit(
                        linked.Id, linkedRole, [.. lines], section.AuthoredField, section.AuthoredValue, linkedName: null, name: linked.SectionName));
                    continue;
                }

                if (!HasSameLines(current, section.Lines))
                {
                    changedSinceLinks.Add(linked.Name);
                }

                sections.Add(new ProductUsageEdit(
                    linked.Id,
                    linkedRole,
                    [.. lines],
                    section.AuthoredField,
                    section.AuthoredValue,
                    linked.Name,
                    [.. section.AddIns.Select(a => new SectionLine(ingredientsById[a.IngredientId], a.Grams))],
                    sectionLabelOf(linked.Name, linkedRole),
                    linkedOwner: PricedSection.LinkedOwnerOf(linked)));
                continue;
            }

            // The product's own, or a linked one deleted since: the version's lines, and a linked one's add-ins with them,
            // since a section of its own has none.
            var keepsId = section.IsOwn && ownedNow.Contains(section.SubAssemblyId);
            sections.Add(new ProductUsageEdit(
                keepsId ? section.SubAssemblyId : 0,
                roles.First(r => r.Id == section.Role.Id),
                [
                    .. section.Lines.Select(l => new SectionLine(ingredientsById[l.IngredientId], l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)),
                    .. section.AddIns.Select(a => new SectionLine(ingredientsById[a.IngredientId], a.Grams)),
                ],
                section.AuthoredField,
                section.AuthoredValue,
                linkedName: null,
                name: section.LabelIn(content.ProductName, content.Schema),
                isOwn: section.IsOwn && !keepsId));
        }

        return new Restored(ProductRecipeDraft.KeyedAsIn(sections, content.Sections), changedSinceLinks, ownNow);
    }

    /// <summary>Whether a sub-assembly's lines now are the ones a version saw: the same ingredients, grams and base ticks, in order.</summary>
    private static bool HasSameLines(List<SubAssemblyLine> now, IReadOnlyList<VersionLine> then) =>
        now.Count == then.Count
        && now.Zip(then).All(pair =>
            pair.First.IngredientId == pair.Second.IngredientId
            && pair.First.Grams == pair.Second.Grams
            && pair.First.IsFormulaBase == pair.Second.IsFormulaBase);
}
