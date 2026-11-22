using System.Collections.ObjectModel;
using System.Collections.Specialized;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
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
    /// different is rebuilt. One whose role was made product-owned, or no longer, is the product's own now, or linked, but
    /// not while it's changed here (<see cref="IsChangedHere"/>): it stays as it's being edited, and Save is refused (ADR
    /// 0007). The screen points the packaging rows at their refreshed materials the same way after. True when
    /// a linked section came in changed from where it's kept, which the undo history then follows (<see cref="StepAsSavedNow"/>).
    /// </summary>
    /// <param name="form">The section form as it stands: what's typed into it is changed here too, applied or not.</param>
    internal bool RefreshRows(IReadOnlyList<SubAssembly> linkedSubAssemblies, IReadOnlyList<SubAssemblyLine> linkedLines, SectionFormSnapshot? form = null)
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
            if (saved is not null && edit.IsOwned != isOwnNow && IsChangedHere(edit, i, form))
            {
                fresh = edit.WithIngredients(lines => Repriced(lines, ingredientsById));
            }
            else if (saved is not null && edit.IsLinked && isOwnNow)
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

                fresh = AsSavedNow(edit, subAssembly, linkedLinesById, ingredientsById, form?.HasLinesTypedInto(i, edit) == true);
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
    /// Whether a saved section holds a change of the user's: something Save would write of it that isn't as the recipe was
    /// saved, or something typed into its form and not applied yet. A linked one the saved recipe doesn't link (a version
    /// put back links it) holds one only once its lines are changed here.
    /// </summary>
    private bool IsChangedHere(ProductUsageEdit edit, int index, SectionFormSnapshot? form)
    {
        if (form?.IsTypedInto(index, edit) == true)
        {
            return true;
        }

        return edit.IsLinked
            ? (_savedRecipe.Linked.FirstOrDefault(l => l.SubAssemblyId == edit.SubAssemblyId) is { } linked ? !SavesSame(LinkedDraftOf(edit), linked) : edit.AreLinkedLinesEdited)
            : _savedRecipe.New.FirstOrDefault(n => n.Id == edit.SubAssemblyId) is not { } own || !SavesSame(OwnDraftOf(edit), own);
    }

    /// <summary>
    /// A linked section as the sub-assembly where it's kept is saved now (<paramref name="subAssembly"/>): its lines, name
    /// and role come from there; its add-ins are this product's own, so they only need their ingredients' new costs; lines
    /// changed here and not saved yet stay, repriced, and with them, as with lines typed in its form
    /// (<paramref name="areLinesTypedInto"/>), the lines they were changed from; its piece size stays the product's.
    /// </summary>
    internal ProductUsageEdit AsSavedNow(
        ProductUsageEdit edit, SubAssembly subAssembly, ILookup<int, SubAssemblyLine> linkedLinesById, Dictionary<int, RawMaterial> ingredientsById,
        bool areLinesTypedInto = false)
    {
        var role = _roles.FirstOrDefault(r => r.Id == subAssembly.RoleId) ?? subAssembly.Role;
        return edit.WithLinked(
            subAssembly.Name,
            SectionLabelOf(subAssembly.Name, role),
            role,
            [.. linkedLinesById[subAssembly.Id].Select(l => new SectionLine(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
            edit.AreLinkedLinesEdited ? Repriced(edit.Lines, ingredientsById) : null,
            Repriced(edit.AddIns, ingredientsById),
            PricedSection.LinkedOwnerOf(subAssembly),
            areLinesTypedInto);
    }

    /// <summary>
    /// An undo step's recipe (<paramref name="step"/>) with its linked sections as a load just found them saved where
    /// they're kept (as <see cref="RefreshRows"/> brought them into the form), and the section form open on one of them
    /// showing its lines as saved now, unless they're typed into there. What the product holds of its own (a piece size,
    /// add-ins, lines edited here and the ones they were changed from) stays as the step had it. An undo or a redo then
    /// never puts back a sub-assembly's lines as they were before it was saved on the Sub-Assemblies screen.
    /// </summary>
    internal RecipeSnapshot StepAsSavedNow(RecipeSnapshot step, IReadOnlyList<SubAssembly> linkedSubAssemblies, IReadOnlyList<SubAssemblyLine> linkedLines)
    {
        var ingredientsById = _ingredients.ToDictionary(m => m.Id);
        var linkedById = linkedSubAssemblies.ToDictionary(s => s.Id);
        var linkedLinesById = linkedLines.ToLookup(l => l.SubAssemblyId);
        var productId = _host.DetailProductId;
        List<ProductUsageEdit> usages = [.. step.Usages];
        var form = step.Form;
        var changed = false;
        for (var i = 0; i < usages.Count; i++)
        {
            var edit = usages[i];
            if (!edit.IsLinked
                || linkedById.GetValueOrDefault(edit.SubAssemblyId) is not { } saved
                || (saved.OwnerProductId is { } ownerId && ownerId == productId))
            {
                continue;
            }

            var fresh = AsSavedNow(edit, saved, linkedLinesById, ingredientsById, step.Form.HasLinesTypedInto(i, edit));
            if (fresh.ShowsSameAs(edit))
            {
                continue;
            }

            usages[i] = fresh;
            changed = true;
            if (form is { IsAdding: false, EditingUsageIndex: { } at } && at == i && !edit.AreLinkedLinesEdited && form.ShowsLines(edit.Lines))
            {
                IReadOnlyList<IngredientLineSnapshot> lines =
                    [.. fresh.Lines.Select(l => new IngredientLineSnapshot(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];
                // The figure typed stays and the other follows the new batch, as the form's own does (FollowPieceSize).
                var batchWeight = lines.Sum(l => l.Grams ?? 0m) + form.AddIns.Sum(l => l.Grams ?? 0m);
                var typed = form.AuthoredField == UsageAuthoredField.WeightPerPiece ? form.WeightPerPiece : form.Yield;
                var (weightPerPiece, yield) = ProductRules.ResolvePieceSize(form.AuthoredField, typed, batchWeight);
                form = form with
                {
                    Title = fresh.EditTitle,
                    Lines = lines,
                    FormulaBase = SubAssemblyRules.FormulaBaseWeight([.. lines.Select(l => (l.Grams ?? 0m, l.IsFormulaBase))]),
                    WeightPerPiece = form.AuthoredField == UsageAuthoredField.WeightPerPiece ? form.WeightPerPiece : Math.Round(weightPerPiece, 2, MidpointRounding.ToEven),
                    Yield = form.AuthoredField == UsageAuthoredField.Yield ? form.Yield : Math.Round(yield, 2, MidpointRounding.ToEven),
                };
            }
        }

        return changed ? step with { Usages = usages, Form = form } : step;
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

    /// <summary>
    /// An undo step's sections put back (<see cref="ReplaceUsageItems"/>), each at the ingredients as the screen last loaded
    /// them, by id, as a load points the sections at them (<see cref="Repriced"/>). A step keeps the copies it was taken
    /// with, so without this a section put back after a load that moved its ingredients' costs would show the old costs,
    /// the header's figures too, until the next load. A linked section a load found changed where it's kept is already as
    /// kept in every step (StepAsSavedNow); its lines and add-ins are repriced here like any other's.
    /// </summary>
    internal void Restore(IReadOnlyList<ProductUsageEdit> sections)
    {
        var ingredientsById = _ingredients.ToDictionary(m => m.Id);
        ReplaceUsageItems([.. sections.Select(section => section.WithIngredients(lines => Repriced(lines, ingredientsById)))]);
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
        new([.. UsageEdits.Where(u => u.IsEditable).Select(OwnDraftOf)], [.. UsageEdits.Where(u => u.IsLinked).Select(LinkedDraftOf)])
        {
            Components = _host.IsEditingComposite ? [.. ProductComponentRow.CompleteLines(_components.ComponentEdits).Select(c => (c.Product.Id, c.Quantity))] : [],
            OwnTaken = SavedOwnSectionIds,
        };

    /// <summary>A section whose lines the form edits, the product's own or a new one, as Save writes it.</summary>
    private static NewSubAssemblyDraft OwnDraftOf(ProductUsageEdit u) =>
        new(u.RoleId, [.. u.Lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))], u.AuthoredField, u.AuthoredValue, u.Name)
        {
            // The product's own keeps its id, so Save rewrites it in place; a new section's is 0.
            Id = u.SubAssemblyId,
            Placement = u.ToMadeIngredient is { } made ? new SectionPlacement.MoveToInHouse(made)
                : u.IsReleasing ? new SectionPlacement.MoveToSubAssemblies()
                : u.IsOwned ? new SectionPlacement.ProductsOwn()
                : new SectionPlacement.ByRole(),
        };

    /// <summary>A linked section as Save writes it.</summary>
    private static LinkedUsageDraft LinkedDraftOf(ProductUsageEdit u) =>
        new(u.SubAssemblyId, u.AuthoredField, u.AuthoredValue)
        {
            AddIns = [.. u.AddIns.Select(a => (a.Ingredient.Id, a.Grams))],

            // Only lines changed here are written, so a product's Save doesn't put back lines the Sub-Assemblies
            // screen has changed since; and with those, the lines they were changed from, so it's refused over others.
            Lines = u.AreLinkedLinesEdited
                ? [.. u.Lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))]
                : null,
            LinesTaken = u.AreLinkedLinesEdited
                ? [.. u.LinesTaken.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))]
                : null,
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
        && a.Linked.Zip(b.Linked).All(pair => SavesSame(pair.First, pair.Second))
        && a.New.Count == b.New.Count
        && a.New.Zip(b.New).All(pair => SavesSame(pair.First, pair.Second));

    private static bool SavesSame(LinkedUsageDraft a, LinkedUsageDraft b) =>
        a.SubAssemblyId == b.SubAssemblyId
        && a.AuthoredField == b.AuthoredField
        && a.AuthoredValue == b.AuthoredValue
        && a.AddIns.SequenceEqual(b.AddIns)
        && (a.Lines is null) == (b.Lines is null)
        && (a.Lines ?? []).SequenceEqual(b.Lines ?? []);

    private static bool SavesSame(NewSubAssemblyDraft a, NewSubAssemblyDraft b) =>
        a.Id == b.Id
        && a.Placement == b.Placement
        && a.RoleId == b.RoleId
        && a.SectionName == b.SectionName
        && a.AuthoredField == b.AuthoredField
        && a.AuthoredValue == b.AuthoredValue
        && a.Lines.SequenceEqual(b.Lines);
}
