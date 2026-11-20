using System.Collections.ObjectModel;
using System.Collections.Specialized;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>What a product's recipe draft (<see cref="ProductRecipeDraft"/>) asks of its form: the product it's for, and its kind.</summary>
public interface IProductRecipeDraftHost
{
    /// <summary>Whether the product is a composite, whose products the recipe Save writes takes too.</summary>
    bool IsEditingComposite { get; }

    /// <summary>The product whose recipe the form holds, or null for a new one: what a sub-assembly it owns is told by.</summary>
    int? DetailProductId { get; }

    /// <summary>The product's name as typed: what a linked section's name goes by less (SectionLabelOf).</summary>
    string EditingName { get; }
}

/// <summary>
/// A product's recipe as its form edits it (ADR 0002): its sections, each a sub-assembly it holds (its own, linked, or new),
/// in recipe order, and the recipe as it was saved, which Save compares against. It holds the rules on the sections (kept
/// in recipe order, told apart, each marked when its role has others), a linked section copied as a new one, a load's
/// re-pointing at fresh ingredients, roles and linked sub-assemblies (<see cref="RefreshRows"/>, ADR 0007), and what Save
/// writes of it (<see cref="CurrentDraft"/>, <see cref="IsAsSaved"/>). It reads nothing and asks nothing: the recipe part
/// (<see cref="ProductRecipe"/>) has the tabs that mirror the sections, the commands and the section form's flow, each a
/// change of one undo step.
/// </summary>
public sealed class ProductRecipeDraft : ObservableState
{
    private readonly IProductRecipeDraftHost _host;
    private readonly IReadOnlyList<RawMaterial> _ingredients;
    private readonly IReadOnlyList<SubAssemblyRole> _roles;
    private readonly ProductComponentsDraft _components;

    // What the product had when it was loaded, so Save only sends the parts that changed.
    private RecipeDraft _savedRecipe = new([], []);

    /// <param name="host">The form: the product it's for, and its kind.</param>
    /// <param name="ingredients">Every ingredient, as the screen last loaded them: what a section's lines point at.</param>
    /// <param name="roles">Every sub-assembly role, as the screen last loaded them.</param>
    /// <param name="components">A composite's products, whose lines are part of the recipe Save writes.</param>
    public ProductRecipeDraft(
        IProductRecipeDraftHost host, IReadOnlyList<RawMaterial> ingredients, IReadOnlyList<SubAssemblyRole> roles, ProductComponentsDraft components)
    {
        _host = host;
        _ingredients = ingredients;
        _roles = roles;
        _components = components;

        // As the sections change: the part's tabs mirror them first, then each section is marked when others share its role,
        // so it's called by its name too, then the tabs read their headers again, in the order they always went.
        UsageEdits.CollectionChanged += (_, e) =>
        {
            SectionsChanged?.Invoke(e);
            MarkRepeatedRoles();
            RepeatedRolesMarked?.Invoke();
        };
    }

    /// <summary>The sections changed: what follows them first (the part's tabs, ProductRecipe.MirrorUsageTabs), before the roles are marked.</summary>
    internal event Action<NotifyCollectionChangedEventArgs>? SectionsChanged;

    /// <summary>Each section has been marked when others share its role: what reads it next (the tabs' headers).</summary>
    internal event Action? RepeatedRolesMarked;

    /// <summary>Every sub-assembly, saved or not, its own and linked, in recipe order (ProductUsageEdit.RecipeOrder).</summary>
    public ObservableCollection<ProductUsageEdit> UsageEdits { get; } = [];

    /// <summary>Every ingredient, as the screen last loaded them: what a section's lines point at.</summary>
    public IReadOnlyList<RawMaterial> Ingredients => _ingredients;

    /// <summary>Every sub-assembly role, as the screen last loaded them.</summary>
    public IReadOnlyList<SubAssemblyRole> Roles => _roles;

    /// <summary>The saved usages as sections, in recipe order, and what Save compares the recipe against from here.</summary>
    internal void Show(
        IReadOnlyList<ProductSubAssemblyUsage> usages, IReadOnlyList<SubAssemblyLine> subAssemblyLines, IReadOnlyList<UsageAddInLine> addIns)
    {
        var linesBySubAssembly = subAssemblyLines.ToLookup(l => l.SubAssemblyId);

        // Add-ins key on the usage, not the sub-assembly: they're this product's own, so two products
        // linking the same one have different sets.
        var addInsByUsage = addIns.ToLookup(a => a.UsageId);
        var ingredientsById = _ingredients.ToDictionary(m => m.Id);
        List<ProductUsageEdit> sections =
        [
            .. usages
                .Select(u => ToUsageEdit(u, linesBySubAssembly[u.SubAssemblyId], addInsByUsage[u.Id], ingredientsById))
                .Order(ProductUsageEdit.RecipeOrder),
        ];

        ReplaceUsageItems(sections);
        _savedRecipe = CurrentDraft();
    }

    /// <summary>
    /// No recipe saved to compare against, and, unless <paramref name="keepSections"/>, no sections: with it, they stay for
    /// another product's to replace in the same pass (<see cref="Show"/>), so the tabs mirroring them aren't all built again.
    /// </summary>
    internal void Clear(bool keepSections)
    {
        if (!keepSections)
        {
            UsageEdits.Clear();
        }

        _savedRecipe = new RecipeDraft([], []);
    }

    /// <summary>
    /// Tells each section whether the recipe has others in its role, so it's called by its name too ("Filling: Ube"). Run
    /// whenever the recipe's sections change; the part's tabs read it after.
    /// </summary>
    private void MarkRepeatedRoles()
    {
        var countsByRole = UsageEdits.CountBy(u => u.RoleId).ToDictionary();
        foreach (var usage in UsageEdits)
        {
            usage.IsRoleRepeated = countsByRole[usage.RoleId] > 1;
        }
    }

    /// <summary>The sections the product keeps as its own, as it was saved: the ids a restored version's own sections are told by.</summary>
    internal IReadOnlySet<int> SavedOwnSectionIds => _savedRecipe.New.Select(n => n.Id).Where(id => id != 0).ToHashSet();

    /// <summary>
    /// A linked section as a new one whose lines the form edits, at the same role and piece size (Make a Copy, Unlink):
    /// <paramref name="isOwn"/> makes it the product's own whatever its role.
    /// </summary>
    internal static ProductUsageEdit CopyOfLinked(ProductUsageEdit usage, bool isOwn) =>
        // Its add-ins become plain lines of the copy: the batch it was showing is the batch it keeps.
        // Never a duplicate ingredient, since an add-in can't be one the recipe already has.
        // In a role the recipe has more than one in, it keeps what it went by, so it's still told apart.
        new(0, usage.Role, [.. usage.Lines, .. usage.AddIns], usage.AuthoredField, usage.AuthoredValue,
            linkedName: null, name: usage.IsRoleRepeated ? usage.Label : null, isOwn: isOwn);

    /// <summary>
    /// Points the recipe's sections, saved or not, at the refreshed ingredients and roles, and a linked
    /// section at its refreshed sub-assembly and lines, keeping each section's own piece size, so costs
    /// changed on other tabs reach the recipe and the cost summary. Only a section that shows something
    /// different is rebuilt. The screen points the packaging rows at their refreshed materials the same way after. True when
    /// a linked section came in changed from where it's kept, which the undo history then follows (<see cref="ProductRecipe.WithLinkedAsSavedNow"/>).
    /// </summary>
    internal bool RefreshRows(IReadOnlyList<SubAssembly> linkedSubAssemblies, IReadOnlyList<SubAssemblyLine> linkedLines)
    {
        var ingredientsById = _ingredients.ToDictionary(m => m.Id);
        var linkedById = linkedSubAssemblies.ToDictionary(s => s.Id);
        var linkedLinesById = linkedLines.ToLookup(l => l.SubAssemblyId);
        var roles = _roles;
        var productId = _host.DetailProductId;

        var changed = false;
        var linkedChanged = false;
        for (var i = 0; i < UsageEdits.Count; i++)
        {
            var edit = UsageEdits[i];
            ProductUsageEdit fresh;
            var saved = edit.SubAssemblyId > 0 ? linkedById.GetValueOrDefault(edit.SubAssemblyId) : null;
            var isOwnNow = saved is not null && saved.OwnerProductId is { } ownerId && ownerId == productId;
            if (saved is not null && edit.IsLinked && isOwnNow)
            {
                // Its role was made product-owned: the product owns it now, with any add-ins it had merged into its lines.
                var role = roles.FirstOrDefault(r => r.Id == saved.RoleId) ?? saved.Role;
                fresh = new ProductUsageEdit(
                    saved.Id,
                    role,
                    [.. linkedLinesById[saved.Id].Select(l => new SectionLine(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                    edit.AuthoredField,
                    edit.AuthoredValue,
                    linkedName: null,
                    name: saved.SectionName);
            }
            else if (edit.IsLinked || (edit.IsOwned && saved is not null && !isOwnNow))
            {
                // A linked one, or one of its own whose role is no longer product-owned, which it links now.
                if (saved is not { } subAssembly)
                {
                    continue;
                }

                fresh = AsSavedNow(edit, subAssembly, linkedLinesById, ingredientsById);
            }
            else
            {
                fresh = edit.With(roles.FirstOrDefault(r => r.Id == edit.RoleId) ?? edit.Role, Repriced(edit.Lines, ingredientsById));
            }

            if (!fresh.ShowsSameAs(edit))
            {
                UsageEdits[i] = fresh;
                changed = true;
                linkedChanged |= edit.IsLinked;
            }
        }

        // A renamed role or sub-assembly can move a section.
        if (changed)
        {
            var sorted = UsageEdits.Order(ProductUsageEdit.RecipeOrder).ToList();
            for (var i = 0; i < sorted.Count; i++)
            {
                var from = UsageEdits.IndexOf(sorted[i]);
                if (from != i)
                {
                    UsageEdits.Move(from, i);
                }
            }
        }

        return linkedChanged;
    }

    /// <summary>
    /// A linked section as the sub-assembly where it's kept is saved now (<paramref name="subAssembly"/>): its lines, name
    /// and role come from there; its add-ins are this product's own, so they only need their ingredients' new costs; lines
    /// changed here and not saved yet stay, repriced; its piece size stays the product's.
    /// </summary>
    internal ProductUsageEdit AsSavedNow(
        ProductUsageEdit edit, SubAssembly subAssembly, ILookup<int, SubAssemblyLine> linkedLinesById, Dictionary<int, RawMaterial> ingredientsById)
    {
        var role = _roles.FirstOrDefault(r => r.Id == subAssembly.RoleId) ?? subAssembly.Role;
        return edit.WithLinked(
            subAssembly.Name,
            SectionLabelOf(subAssembly.Name, role),
            role,
            [.. linkedLinesById[subAssembly.Id].Select(l => new SectionLine(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
            edit.AreLinkedLinesEdited ? Repriced(edit.Lines, ingredientsById) : null,
            Repriced(edit.AddIns, ingredientsById),
            PricedSection.LinkedOwnerOf(subAssembly));
    }

    /// <summary><paramref name="lines"/> with each ingredient swapped for its refreshed copy when that differs; unchanged lines are kept as they are.</summary>
    internal static IReadOnlyList<SectionLine> Repriced(IReadOnlyList<SectionLine> lines, Dictionary<int, RawMaterial> ingredientsById) =>
        [.. lines.Select(l => ingredientsById.TryGetValue(l.Ingredient.Id, out var fresh) && !CollectionMerge.HasSameValues(fresh, l.Ingredient)
            ? new SectionLine(fresh, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)
            : l)];

    /// <summary>
    /// Refuses <paramref name="usage"/> in place of <paramref name="replacing"/> when the recipe could no longer tell its
    /// sections apart: two in one role by the same name, or both without one (SubAssemblyRules.EnsureSectionsDistinct),
    /// or a name too long. SetRecipeAsync checks the same as it saves; this is what puts the message up the moment
    /// Apply is pressed, or the tab is left.
    /// </summary>
    internal void EnsureSectionsDistinct(ProductUsageEdit usage, ProductUsageEdit? replacing)
    {
        List<ProductUsageEdit> sections = [.. UsageEdits.Where(u => !ReferenceEquals(u, replacing)), usage];
        SubAssemblyRules.EnsureSectionsDistinct([.. sections.Select(u => (u.RoleId, u.RoleName, u.Label))]);
        SubAssemblyRules.EnsureValidSectionName(usage.IsEditable ? usage.Name : null);
    }

    /// <summary>Puts <paramref name="usage"/> where it sorts among the sections (ProductUsageEdit.RecipeOrder).</summary>
    internal void InsertInRecipeOrder(ProductUsageEdit usage)
    {
        var index = 0;
        while (index < UsageEdits.Count && ProductUsageEdit.RecipeOrder.Compare(UsageEdits[index], usage) <= 0)
        {
            index++;
        }

        UsageEdits.Insert(index, usage);
    }

    /// <summary>
    /// Puts <paramref name="replacement"/> in <paramref name="old"/>'s place, then moves it to where it sorts
    /// (a new name or role can move it). Replacing rather than removing keeps its tab, and the tab showing.
    /// </summary>
    internal void ReplaceInRecipeOrder(ProductUsageEdit old, ProductUsageEdit replacement)
    {
        var index = UsageEdits.IndexOf(old);
        if (index < 0)
        {
            InsertInRecipeOrder(replacement);
            return;
        }

        UsageEdits[index] = replacement;
        var sortedIndex = UsageEdits.Count(u => !ReferenceEquals(u, replacement) && ProductUsageEdit.RecipeOrder.Compare(u, replacement) <= 0);
        if (sortedIndex != index)
        {
            UsageEdits.Move(index, sortedIndex);
        }
    }

    /// <summary>
    /// Makes UsageEdits hold <paramref name="sections"/>, section for section and in place: each tab stays and shows its
    /// new section (MirrorUsageTabs), so the tabs, the one showing included, stay the same objects, and only the
    /// difference is added or taken away. Never Clear(), which the mirror reads as every tab going. It leaves _savedRecipe
    /// alone, which is why an undo can use it and Show can't be reused.
    /// </summary>
    internal void ReplaceUsageItems(IReadOnlyList<ProductUsageEdit> sections)
    {
        for (var i = 0; i < sections.Count; i++)
        {
            if (i < UsageEdits.Count)
            {
                UsageEdits[i] = sections[i];
            }
            else
            {
                UsageEdits.Add(sections[i]);
            }
        }

        while (UsageEdits.Count > sections.Count)
        {
            UsageEdits.RemoveAt(UsageEdits.Count - 1);
        }
    }

    /// <summary>A saved usage as its section, with the screen's own copies of its role and ingredients (ProductUsageEdit.FromSaved).</summary>
    private ProductUsageEdit ToUsageEdit(
        ProductSubAssemblyUsage usage,
        IEnumerable<SubAssemblyLine> lines,
        IEnumerable<UsageAddInLine> addIns,
        IReadOnlyDictionary<int, RawMaterial> ingredientsById)
    {
        var subAssembly = usage.SubAssembly!;
        var role = _roles.FirstOrDefault(r => r.Id == subAssembly.RoleId) ?? subAssembly.Role;
        return ProductUsageEdit.FromSaved(usage, lines, addIns, role, _host.EditingName, ingredientsById);
    }

    /// <summary>
    /// What a linked sub-assembly goes by beside its role in this product's recipe: its name, less the product's own and
    /// the role it was saved under from here ("Ube" for "Cheese Roll Filling: Ube"), as SetRecipeAsync reads it.
    /// </summary>
    internal string? SectionLabelOf(string subAssemblyName, SubAssemblyRole? role) =>
        SubAssemblyRules.SectionLabel(subAssemblyName, _host.EditingName, role?.Name ?? string.Empty);

    /// <summary>
    /// The recipe as Save writes it: the new sections with their lines, and the sub-assemblies it links; and a composite's
    /// products, each with how many go in (a line half filled in is refused: ProductComponentRow.CompleteLines).
    /// </summary>
    internal RecipeDraft CurrentDraft() =>
        new(
            [.. UsageEdits.Where(u => u.IsEditable).Select(u => new NewSubAssemblyDraft(
                u.RoleId, [.. u.Lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))], u.AuthoredField, u.AuthoredValue, u.Name)
            {
                // The product's own keeps its id, so Save rewrites it in place; a new section's is 0.
                Id = u.SubAssemblyId,
                Placement = u.ToMadeIngredient is { } made ? new SectionPlacement.MoveToInHouse(made)
                    : u.IsReleasing ? new SectionPlacement.MoveToSubAssemblies()
                    : u.IsOwned ? new SectionPlacement.ProductsOwn()
                    : new SectionPlacement.ByRole(),
            })],
            [.. UsageEdits.Where(u => u.IsLinked).Select(u => new LinkedUsageDraft(u.SubAssemblyId, u.AuthoredField, u.AuthoredValue)
            {
                AddIns = [.. u.AddIns.Select(a => (a.Ingredient.Id, a.Grams))],

                // Only lines changed here are written, so a product's Save doesn't put back lines the Sub-Assemblies
                // screen has changed since.
                Lines = u.AreLinkedLinesEdited
                    ? [.. u.Lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))]
                    : null,
            })])
        {
            Components = _host.IsEditingComposite ? [.. ProductComponentRow.CompleteLines(_components.ComponentEdits).Select(c => (c.Product.Id, c.Quantity))] : [],
        };

    /// <summary>Whether <paramref name="draft"/> would save the recipe as it was loaded (IsSameRecipe), so Save needn't write it.</summary>
    internal bool IsAsSaved(RecipeDraft draft) => IsSameRecipe(draft, _savedRecipe);

    /// <summary>
    /// Whether two recipes would save the same: the same sub-assemblies, roles, lines, add-ins and piece
    /// sizes, and the same products at the same quantities, in the same order. A linked draft is compared field by
    /// field rather than as a record, as a new one already is: a record's own Equals compares its AddIns list by
    /// reference, which no two recipes built separately ever share, so Save would write the recipe every time.
    /// </summary>
    private static bool IsSameRecipe(RecipeDraft a, RecipeDraft b) =>
        a.Components.SequenceEqual(b.Components)
        && a.Linked.Count == b.Linked.Count
        && a.Linked.Zip(b.Linked).All(pair =>
            pair.First.SubAssemblyId == pair.Second.SubAssemblyId
            && pair.First.AuthoredField == pair.Second.AuthoredField
            && pair.First.AuthoredValue == pair.Second.AuthoredValue
            && pair.First.AddIns.SequenceEqual(pair.Second.AddIns)
            && (pair.First.Lines is null) == (pair.Second.Lines is null)
            && (pair.First.Lines ?? []).SequenceEqual(pair.Second.Lines ?? []))
        && a.New.Count == b.New.Count
        && a.New.Zip(b.New).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Placement == pair.Second.Placement

            && pair.First.RoleId == pair.Second.RoleId
            && pair.First.SectionName == pair.Second.SectionName
            && pair.First.AuthoredField == pair.Second.AuthoredField
            && pair.First.AuthoredValue == pair.Second.AuthoredValue
            && pair.First.Lines.SequenceEqual(pair.Second.Lines));
}
