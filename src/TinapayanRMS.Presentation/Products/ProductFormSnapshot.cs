using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// Which of the recipe's tabs was showing, by kind, so a tab rebuilt from scratch can be found again: one case per
/// kind of <see cref="RecipeTab"/>, and null for none.
/// </summary>
public closed record RecipeTabChoice
{
    /// <summary>A sub-assembly's tab, the <paramref name="Index"/>th of them.</summary>
    public sealed record Usage(int Index) : RecipeTabChoice;

    public sealed record AddSubAssembly : RecipeTabChoice;

    public sealed record Packaging : RecipeTabChoice;

    public sealed record Components : RecipeTabChoice;
}

/// <summary>
/// One line of a composite's Products table, by product id rather than the product, as <see cref="IngredientLineSnapshot"/>
/// keeps its ingredient: a refresh swaps in fresh products and recipe costs, which a repriced line isn't an edit of.
/// </summary>
public readonly record struct ComponentLineSnapshot(int? ProductId, int? Quantity);

/// <summary>
/// The Products screen's editable state at one instant: everything one undo step puts back, and nothing else. The
/// product's own fields and its costing are the screen's; the recipe, the packaging and a composite's products are each
/// their part's own share, which that part keeps and puts back (ProductRecipe, ProductPackaging and ProductComponents'
/// Capture and Restore), in the order the screen's RestoreSnapshot puts them back in.
/// Deliberately absent are Save's dirty-check baselines (what the parts keep as saved: the recipe as loaded, the Packaging
/// tab's lines, bulk lines and link, the Costing tab's percentages and defaults), which decide what Save writes rather than
/// what the screen shows; the picked product, which an undo never changes; and everything derived (RecipeTabs, Costs),
/// which the recompute at the end of a restore works out again.
/// </summary>
public sealed record ProductFormSnapshot : IFormSnapshot<ProductFormSnapshot>
{
    // The product
    public required int EditingId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required decimal? CurrentPrice { get; init; }

    /// <summary>How many finished pieces one sellable unit holds (Product.QuantityRequired); 1 by the piece.</summary>
    public required int QuantityRequired { get; init; }

    /// <summary>A composite's products bring their own packaging too (Product.IncludesComponentPackaging).</summary>
    public required bool IncludesComponentPackaging { get; init; }

    /// <summary>By id: assigning the category object runs the picker's "add new category" sentinel.</summary>
    public required int? CategoryId { get; init; }
    public required string CloneName { get; init; }

    public required (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin) Costing { get; init; }

    /// <summary>The recipe: its sections, the tab showing and the section form (ProductRecipe's own share).</summary>
    public required RecipeSnapshot Recipe { get; init; }

    /// <summary>The Packaging tab: its link, its lines and its bulk packaging (ProductPackaging's own share).</summary>
    public required PackagingSnapshot Packaging { get; init; }

    /// <summary>A composite's Products table; empty for a product made of sub-assemblies, which has no such table.</summary>
    public required IReadOnlyList<ComponentLineSnapshot> Components { get; init; }

    /// <summary>
    /// True when two snapshots would look the same to the user, so nothing is worth recording. Written out
    /// rather than left to the record's own equality because the list members compare by reference there, and
    /// because the view-only members (the tab showing) are restored but mustn't count as a change. Each part's share
    /// says for itself (RecipeSnapshot.ShowsSameAs, PackagingSnapshot.ShowsSameAs).
    /// </summary>
    public bool ShowsSameAs(ProductFormSnapshot other) =>
        EditingId == other.EditingId
        && Name == other.Name
        && Description == other.Description
        && CurrentPrice == other.CurrentPrice
        && QuantityRequired == other.QuantityRequired
        && IncludesComponentPackaging == other.IncludesComponentPackaging
        && CategoryId == other.CategoryId
        && CloneName == other.CloneName
        && Costing == other.Costing
        && Recipe.ShowsSameAs(other.Recipe)
        && Packaging.ShowsSameAs(other.Packaging)
        && Components.SequenceEqual(other.Components);

    /// <summary>
    /// True when two snapshots would be saved the same, which is what the Save button is enabled by. It's
    /// <see cref="ShowsSameAs"/> bar the Create Variant name, which no Save writes: that box belongs to the
    /// button beside it. Everything else counts, the open sub-assembly form included, since a Save applies it
    /// on the way in. Which section the form is pointed at isn't the user's change, so the screen moves what it
    /// measures against on to the form showing (<see cref="WithFormOf"/>) whenever one opens over another.
    /// </summary>
    public bool SavesSameAs(ProductFormSnapshot other) =>
        (this with { CloneName = other.CloneName }).ShowsSameAs(other);

    /// <summary>
    /// This snapshot with <paramref name="form"/>'s sub-assembly form and recipe tabs in place of its own, and
    /// nothing else of it: the recipe, the packaging, the costing and the product's own fields stay as they were
    /// settled, so a rebase can never hide a change a Save would write.
    /// </summary>
    public ProductFormSnapshot WithFormOf(ProductFormSnapshot form) => this with { Recipe = Recipe.WithFormOf(form.Recipe) };
}

/// <summary>
/// The recipe's share of an undo step (ProductRecipe.Capture): its sections, which of its tabs was showing, whether New
/// Sub-Assembly's tab was there, and the section form.
/// </summary>
public sealed record RecipeSnapshot
{
    /// <summary>Held by reference: a ProductUsageEdit is never changed in place, only replaced.</summary>
    public required IReadOnlyList<ProductUsageEdit> Usages { get; init; }

    /// <summary>
    /// Which tab was showing. Restored, but left out of <see cref="ShowsSameAs"/>: moving between tabs isn't a
    /// change to take back, and the move that does change something — leaving a tab applies its form — is caught
    /// by the section it applied.
    /// </summary>
    public required RecipeTabChoice? SelectedTab { get; init; }
    public required bool HasAddSubAssemblyTab { get; init; }

    /// <summary>The section form, open or not, as it was.</summary>
    public required SectionFormSnapshot Form { get; init; }

    /// <summary>The recipe looks the same: its sections by what the user put there, New Sub-Assembly's tab and the form.</summary>
    public bool ShowsSameAs(RecipeSnapshot other) =>
        SectionsShowSame(Usages, other.Usages)
        && HasAddSubAssemblyTab == other.HasAddSubAssemblyTab
        && Form.ShowsSameAs(other.Form);

    /// <summary>
    /// This one with <paramref name="form"/>'s tab showing, New Sub-Assembly's tab and section form in place of its own:
    /// its sections stay (ProductFormSnapshot.WithFormOf).
    /// </summary>
    public RecipeSnapshot WithFormOf(RecipeSnapshot form) => this with
    {
        SelectedTab = form.SelectedTab,
        HasAddSubAssemblyTab = form.HasAddSubAssemblyTab,
        Form = form.Form,
    };

    /// <summary>
    /// Whether two sets of sections hold the same recipe, by what the user put there: the sub-assembly, its role
    /// and name, its piece size, and each line's ingredient, grams and typed percentage. Prices are deliberately
    /// not compared. A LoadCommand landing mid-gesture swaps every section for a freshly priced copy, and comparing
    /// those — by reference, or through ProductUsageEdit.ShowsSameAs, which reads the ingredient's cost — would
    /// record a step for a reprice the user didn't make, and undoing it would put the stale prices back.
    /// </summary>
    private static bool SectionsShowSame(IReadOnlyList<ProductUsageEdit> left, IReadOnlyList<ProductUsageEdit> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            var (a, b) = (left[i], right[i]);
            if (a.SubAssemblyId != b.SubAssemblyId
                || a.IsOwned != b.IsOwned
                || a.IsReleasing != b.IsReleasing
                || a.ToMadeIngredient != b.ToMadeIngredient

                || a.Role?.Id != b.Role?.Id
                || a.Name != b.Name
                || a.LinkedName != b.LinkedName
                || a.AuthoredField != b.AuthoredField
                || a.AuthoredValue != b.AuthoredValue
                || !LinesShowSame(a.Lines, b.Lines)
                || !LinesShowSame(a.AddIns, b.AddIns))
            {
                return false;
            }
        }

        return true;
    }

    private static bool LinesShowSame(IReadOnlyList<SectionLine> left, IReadOnlyList<SectionLine> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair =>
            pair.First.Ingredient?.Id == pair.Second.Ingredient?.Id
            && pair.First.Grams == pair.Second.Grams
            && pair.First.IsFormulaBase == pair.Second.IsFormulaBase
            && pair.First.AuthoredBakersPercent == pair.Second.AuthoredBakersPercent);
}

/// <summary>
/// The sub-assembly form's share of an undo step (part of the recipe's, RecipeSnapshot.Form): whether it's open, on which
/// section or adding one and how, what's picked and named, its lines and add-ins, and the piece size.
/// </summary>
public sealed record SectionFormSnapshot
{
    public required bool IsOpen { get; init; }
    public required bool IsAdding { get; init; }
    public required string Title { get; init; }
    public required UsageFormMode Mode { get; init; }
    public required bool IsEditingLines { get; init; }
    public required bool IsPickingSource { get; init; }

    /// <summary>Which section the form is editing, as an index into the recipe's sections; null while adding.</summary>
    public required int? EditingUsageIndex { get; init; }
    public required int? RoleId { get; init; }

    /// <summary>Null when the section goes by its role alone, which is the usual case.</summary>
    public required string? Name { get; init; }
    public required int? SourceKey { get; init; }
    public required IReadOnlyList<IngredientLineSnapshot> Lines { get; init; }
    public required IReadOnlyList<IngredientLineSnapshot> AddIns { get; init; }
    public required decimal FormulaBase { get; init; }
    public required UsageAuthoredField AuthoredField { get; init; }
    public required decimal WeightPerPiece { get; init; }
    public required decimal Yield { get; init; }

    /// <summary>Whether the form's lines, as kept, are <paramref name="lines"/>: nothing typed over them there.</summary>
    public bool ShowsLines(IReadOnlyList<SectionLine> lines) =>
        Lines.Count == lines.Count
        && Lines.Zip(lines).All(pair =>
            pair.First.IngredientId == pair.Second.Ingredient.Id
            && pair.First.Grams == pair.Second.Grams
            && pair.First.IsFormulaBase == pair.Second.IsFormulaBase
            && pair.First.AuthoredBakersPercent == pair.Second.AuthoredBakersPercent);

    /// <summary>Whether lines are typed into the form over <paramref name="section"/>, the <paramref name="index"/>th of the recipe's, and not applied.</summary>
    public bool HasLinesTypedInto(int index, ProductUsageEdit section) =>
        IsOpen && !IsAdding && EditingUsageIndex == index && !ShowsLines(section.Lines);

    /// <summary>
    /// Whether anything is typed into the form over <paramref name="section"/>, the <paramref name="index"/>th of the
    /// recipe's, and not applied: lines, add-ins, the piece size, and, for one that's not linked, its role and name.
    /// </summary>
    public bool IsTypedInto(int index, ProductUsageEdit section) =>
        HasLinesTypedInto(index, section)
        || (IsOpen && !IsAdding && EditingUsageIndex == index
            && (!ShowsAddIns(section.AddIns)
                || AuthoredField != section.AuthoredField
                || (AuthoredField == UsageAuthoredField.WeightPerPiece ? WeightPerPiece : Yield) != section.AuthoredValue
                || (!section.IsLinked && (RoleId != section.Role?.Id || Name != section.Name))));

    private bool ShowsAddIns(IReadOnlyList<SectionLine> addIns) =>
        AddIns.Count == addIns.Count
        && AddIns.Zip(addIns).All(pair => pair.First.IngredientId == pair.Second.Ingredient.Id && pair.First.Grams == pair.Second.Grams);

    /// <summary>The form looks the same: every field, its lines and add-ins line by line.</summary>
    public bool ShowsSameAs(SectionFormSnapshot other) =>
        IsOpen == other.IsOpen
        && IsAdding == other.IsAdding
        && Title == other.Title
        && Mode == other.Mode
        && IsEditingLines == other.IsEditingLines
        && IsPickingSource == other.IsPickingSource
        && EditingUsageIndex == other.EditingUsageIndex
        && RoleId == other.RoleId
        && Name == other.Name
        && SourceKey == other.SourceKey
        && Lines.SequenceEqual(other.Lines)
        && AddIns.SequenceEqual(other.AddIns)
        && FormulaBase == other.FormulaBase
        && AuthoredField == other.AuthoredField
        && WeightPerPiece == other.WeightPerPiece
        && Yield == other.Yield;
}

/// <summary>
/// The Packaging tab's share of an undo step (ProductPackagingDraft.Capture): what its packaging links, whether Move to
/// Sub-Assemblies was pressed, its lines for each piece and its bulk packaging, with the set that links.
/// </summary>
public sealed record PackagingSnapshot
{
    public required PackagingLinkTarget? Link { get; init; }

    /// <summary>Move to Sub-Assemblies was pressed on the Packaging tab: Save makes its lines a packaging set, and links it.</summary>
    public required bool IsMoving { get; init; }
    public required IReadOnlyList<PackagingLineSnapshot> Lines { get; init; }

    /// <summary>The packaging set the bulk packaging links, or null while its lines are the product's own (a restored version's are).</summary>
    public PackagingLinkTarget? BulkLink { get; init; }

    /// <summary>The bulk packaging's lines, each for one bulk pack.</summary>
    public required IReadOnlyList<PackagingLineSnapshot> BulkLines { get; init; }

    /// <summary>How many products one bulk pack holds, as typed; null while nothing is.</summary>
    public required int? BulkQuantity { get; init; }

    /// <summary>The packaging looks the same: its links and move, its lines and bulk lines line by line, and the quantity.</summary>
    public bool ShowsSameAs(PackagingSnapshot other) =>
        Equals(Link, other.Link)
        && IsMoving == other.IsMoving
        && Lines.SequenceEqual(other.Lines)
        && Equals(BulkLink, other.BulkLink)
        && BulkLines.SequenceEqual(other.BulkLines)
        && BulkQuantity == other.BulkQuantity;
}
