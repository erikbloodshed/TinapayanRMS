using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>Which of the recipe's tabs was showing, by kind, so a tab rebuilt from scratch can be found again.</summary>
public enum RecipeTabKind
{
    None,
    Usage,
    AddSubAssembly,
    Packaging,
}

/// <param name="UsageIndex">Which sub-assembly's tab, for <see cref="RecipeTabKind.Usage"/>; -1 otherwise.</param>
public readonly record struct RecipeTabChoice(RecipeTabKind Kind, int UsageIndex);

/// <summary>
/// One ingredient line as <see cref="IngredientLineRow.Restore"/> can put it back. Its ingredient travels as
/// an id rather than the object: a LoadCommand landing mid-gesture swaps in freshly priced RawMaterials, and
/// comparing those by reference would read as an edit the user never made.
/// </summary>
public readonly record struct IngredientLineState(int? IngredientId, decimal? Grams, bool IsFormulaBase, decimal? AuthoredBakersPercent);

/// <summary>One packaging line, by material id for the same reason, and by unit id for the unit it's entered in.</summary>
public readonly record struct PackagingLineState(int? PackagingMaterialId, int? UnitId, decimal? QtyPerUnit);

/// <summary>
/// The Products screen's editable state at one instant: everything one undo step puts back, and nothing else.
/// Deliberately absent are Save's dirty-check baselines (_savedRecipe, _savedLines, _savedCosting,
/// _savedPackagingLinkKey, _costingDefaults), which decide what Save writes rather than what the screen shows;
/// the picked product, which an undo never changes; and everything derived (RecipeTabs, Costs), which the
/// recompute at the end of a restore works out again.
/// </summary>
public sealed record ProductFormSnapshot
{
    // The product
    public required int EditingId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required decimal? CurrentPrice { get; init; }

    /// <summary>How many finished pieces one sellable unit holds (Product.QuantityRequired); 1 by the piece.</summary>
    public required int QuantityRequired { get; init; }

    /// <summary>By id: assigning the category object runs the picker's "add new category" sentinel.</summary>
    public required int? CategoryId { get; init; }
    public required string CloneName { get; init; }

    public required (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin, decimal Vat) Costing { get; init; }

    /// <summary>Held by reference: a ProductUsageEdit is never changed in place, only replaced.</summary>
    public required IReadOnlyList<ProductUsageEdit> Usages { get; init; }
    /// <summary>
    /// Which tab was showing. Restored, but left out of <see cref="ShowsSameAs"/>: moving between tabs isn't a
    /// change to take back, and the move that does change something — leaving a tab applies its form — is caught
    /// by the section it applied.
    /// </summary>
    public required RecipeTabChoice SelectedTab { get; init; }
    public required bool HasAddSubAssemblyTab { get; init; }

    // Packaging
    public required bool HasPackaging { get; init; }
    public required PackagingLinkTarget? PackagingLink { get; init; }
    public required IReadOnlyList<PackagingLineState> Lines { get; init; }

    // The sub-assembly form
    public required bool IsUsageFormOpen { get; init; }
    public required bool IsAddingUsage { get; init; }
    public required string UsageFormTitle { get; init; }
    public required UsageFormMode UsageMode { get; init; }
    public required bool IsFormEditingLines { get; init; }
    public required bool IsFormPickingSource { get; init; }

    /// <summary>Which section the form is editing, as an index into <see cref="Usages"/>; null while adding.</summary>
    public required int? EditingUsageIndex { get; init; }
    public required int? UsageRoleId { get; init; }
    /// <summary>Null when the section goes by its role alone, which is the usual case.</summary>
    public required string? UsageName { get; init; }
    public required int? UsageSourceKey { get; init; }
    public required IReadOnlyList<IngredientLineState> FormLines { get; init; }
    public required IReadOnlyList<IngredientLineState> FormAddIns { get; init; }
    public required IReadOnlyList<PackagingLineState> FormPackagingLines { get; init; }
    public required decimal FormFormulaBase { get; init; }
    public required UsageAuthoredField UsageAuthoredField { get; init; }
    public required decimal UsageWeightPerPiece { get; init; }
    public required decimal UsageYield { get; init; }
    public required decimal UsageShrinkagePercent { get; init; }

    /// <summary>
    /// True when two snapshots would look the same to the user, so nothing is worth recording. Written out
    /// rather than left to the record's own equality because the list members compare by reference there, and
    /// because the view-only members (the add-ins table being open) are restored but mustn't count as a change:
    /// a refused apply opens that table to show which line the message is about, and that isn't an edit.
    /// </summary>
    public bool ShowsSameAs(ProductFormSnapshot other) =>
        EditingId == other.EditingId
        && Name == other.Name
        && Description == other.Description
        && CurrentPrice == other.CurrentPrice
        && QuantityRequired == other.QuantityRequired
        && CategoryId == other.CategoryId
        && CloneName == other.CloneName
        && Costing == other.Costing
        && SectionsShowSame(Usages, other.Usages)
        && HasAddSubAssemblyTab == other.HasAddSubAssemblyTab
        && HasPackaging == other.HasPackaging
        && Equals(PackagingLink, other.PackagingLink)
        && Lines.SequenceEqual(other.Lines)
        && IsUsageFormOpen == other.IsUsageFormOpen
        && IsAddingUsage == other.IsAddingUsage
        && UsageFormTitle == other.UsageFormTitle
        && Equals(UsageMode, other.UsageMode)
        && IsFormEditingLines == other.IsFormEditingLines
        && IsFormPickingSource == other.IsFormPickingSource
        && EditingUsageIndex == other.EditingUsageIndex
        && UsageRoleId == other.UsageRoleId
        && UsageName == other.UsageName
        && UsageSourceKey == other.UsageSourceKey
        && FormLines.SequenceEqual(other.FormLines)
        && FormAddIns.SequenceEqual(other.FormAddIns)
        && FormPackagingLines.SequenceEqual(other.FormPackagingLines)
        && FormFormulaBase == other.FormFormulaBase
        && UsageAuthoredField == other.UsageAuthoredField
        && UsageWeightPerPiece == other.UsageWeightPerPiece
        && UsageYield == other.UsageYield
        && UsageShrinkagePercent == other.UsageShrinkagePercent;

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
    public ProductFormSnapshot WithFormOf(ProductFormSnapshot form) => this with
    {
        SelectedTab = form.SelectedTab,
        HasAddSubAssemblyTab = form.HasAddSubAssemblyTab,
        IsUsageFormOpen = form.IsUsageFormOpen,
        IsAddingUsage = form.IsAddingUsage,
        UsageFormTitle = form.UsageFormTitle,
        UsageMode = form.UsageMode,
        IsFormEditingLines = form.IsFormEditingLines,
        IsFormPickingSource = form.IsFormPickingSource,
        EditingUsageIndex = form.EditingUsageIndex,
        UsageRoleId = form.UsageRoleId,
        UsageName = form.UsageName,
        UsageSourceKey = form.UsageSourceKey,
        FormLines = form.FormLines,
        FormAddIns = form.FormAddIns,
        FormPackagingLines = form.FormPackagingLines,
        FormFormulaBase = form.FormFormulaBase,
        UsageAuthoredField = form.UsageAuthoredField,
        UsageWeightPerPiece = form.UsageWeightPerPiece,
        UsageYield = form.UsageYield,
        UsageShrinkagePercent = form.UsageShrinkagePercent,
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
                || a.Role?.Id != b.Role?.Id
                || a.Name != b.Name
                || a.LinkedName != b.LinkedName
                || a.AuthoredField != b.AuthoredField
                || a.AuthoredValue != b.AuthoredValue
                || a.ShrinkagePercent != b.ShrinkagePercent
                || !LinesShowSame(a.Lines, b.Lines)
                || !LinesShowSame(a.AddIns, b.AddIns))
            {
                return false;
            }
        }

        return true;
    }

    private static bool LinesShowSame(IReadOnlyList<SubAssemblyLineEdit> left, IReadOnlyList<SubAssemblyLineEdit> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair =>
            pair.First.Ingredient?.Id == pair.Second.Ingredient?.Id
            && pair.First.Grams == pair.Second.Grams
            && pair.First.IsFormulaBase == pair.Second.IsFormulaBase
            && pair.First.AuthoredBakersPercent == pair.Second.AuthoredBakersPercent);
}
