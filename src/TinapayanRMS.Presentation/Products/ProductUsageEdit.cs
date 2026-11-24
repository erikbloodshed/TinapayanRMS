using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// A linked section's lines as they're kept where it's kept: <paramref name="Now"/>, as last read, and
/// <paramref name="Taken"/>, the ones the lines changed here were changed from, which Save says
/// (LinkedUsageDraft.LinesTaken). The same until lines are both changed here and saved there.
/// </summary>
public sealed record KeptLines(IReadOnlyList<SectionLine> Now, IReadOnlyList<SectionLine> Taken);

/// <summary>
/// A line of a section that is another of the recipe's sections (UsageSectionLine), by that one's key in the form
/// (ProductUsageEdit.Key), in grams, at its place among the section's lines, or a linked one's add-ins.
/// </summary>
/// <param name="AuthoredBakersPercent">The percentage typed for it, or null when its grams were.</param>
public sealed record UsedSectionEdit(long Key, decimal Grams, decimal? AuthoredBakersPercent, int Position);

/// <summary>
/// One sub-assembly in the product's recipe, saved or not: a section of the Recipe Structure, called by its
/// role, and by its name too when the recipe has more than one in that role (<see cref="SectionName"/>), with its
/// ingredient lines, and a box in the Unit Conversion Overview (a batch in pieces). Either new (the form edits its
/// lines, and Save keeps it on the Sub-Assemblies screen), or one the product links (read-only lines, edited on the
/// Sub-Assemblies screen, at the product's own piece size). Its batch totals come from its lines, so they follow its
/// ingredients' costs.
/// </summary>
public sealed class ProductUsageEdit : ObservableRow
{
    /// <summary>
    /// Sections show in role order (SubAssemblyRole.SortOrder); several in one role, in the order they were saved,
    /// then any not saved yet.
    /// </summary>
    public static IComparer<ProductUsageEdit> RecipeOrder { get; } =
        Comparer<ProductUsageEdit>.Create((a, b) => PricedSection.CompareInRecipe(a.Role, a.SubAssemblyId, b.Role, b.SubAssemblyId));

    /// <summary>
    /// A saved usage of <paramref name="productName"/>'s as its section: the sub-assembly's lines, this product's add-ins and
    /// lines that are its other sections, and the piece size, with the name it goes by beside <paramref name="role"/>. The screen hands over its own copies
    /// of the ingredients (<paramref name="ingredientsById"/>), which its pickers share; without them, each line keeps the
    /// ingredient it was read with.
    /// </summary>
    public static ProductUsageEdit FromSaved(
        ProductSubAssemblyUsage usage,
        IEnumerable<SubAssemblyLine> lines,
        IEnumerable<UsageAddInLine> addIns,
        IEnumerable<UsageSectionLine> sectionLines,
        SubAssemblyRole? role,
        string productName,
        IReadOnlyDictionary<int, RawMaterial>? ingredientsById = null)
    {
        var saved = PricedSection.FromSaved(usage, lines, addIns, sectionLines, role, productName, isRoleRepeated: false, ingredientsById);
        return new ProductUsageEdit(
            saved.SubAssemblyId,
            role,
            saved.Lines,
            saved.AuthoredField,
            saved.AuthoredValue,
            saved.LinkedName,
            saved.AddIns,
            saved.Label,
            linkedOwner: saved.LinkedOwner)
        {
            // A saved section is known by its sub-assembly, so a line that is one is too.
            Key = saved.SubAssemblyId,
            SectionLines = [.. saved.SectionLines.Select(l => new UsedSectionEdit(l.SubAssemblyId, l.Grams, l.AuthoredBakersPercent, l.Position))],
            IsUnlocked = usage.IsUnlocked,
        };
    }

    /// <summary>The last key given a section the form made (<see cref="Key"/>): counted down from 0, so never a saved one's id.</summary>
    private static long s_lastNewKey;

    /// <summary>
    /// What tells the section apart from the recipe's others while the form holds it, kept through every change of it (an
    /// Apply, Make a Copy, Unlink, a changed link, a move, a reprice): what another section's line of it names
    /// (<see cref="SectionLines"/>). A section read, or put back from a version, has its sub-assembly's id; one the form makes
    /// gets a new one below 0.
    /// </summary>
    public long Key { get; init; } = NewKey();

    /// <summary>A key no section has had: for one the form makes.</summary>
    internal static long NewKey() => Interlocked.Decrement(ref s_lastNewKey);

    /// <summary>
    /// Its lines that are the recipe's other sections (a sponge on the dough's lines), each by that one's <see cref="Key"/>:
    /// among its <see cref="Lines"/>, or a linked one's <see cref="AddIns"/>, each at its place. The recipe prices them
    /// (ProductRecipeDraft.Priced); <see cref="Figures"/> leaves them out.
    /// </summary>
    public IReadOnlyList<UsedSectionEdit> SectionLines { get; init; } = [];

    /// <summary>
    /// Unlocked while the recipe's other sections use it on their lines (ProductSubAssemblyUsage.IsUnlocked): its lines are
    /// typed into, and it can have pieces of what they leave. Locked, it's only their ingredient.
    /// </summary>
    public bool IsUnlocked { get; init; }

    /// <summary>
    /// Refuses Move to Sub-Assemblies and Move to In-House while the section has others of the recipe on its lines: they're
    /// this product's, so the sub-assembly kept there, and a made ingredient's cost, would go without them.
    /// </summary>
    public void EnsureNoSectionLines()
    {
        if (SectionLines.Count > 0)
        {
            throw new BusinessRuleException(
                $"{SectionName} has another of this recipe's sections on its lines, which can't go with it to Sub-Assemblies. Take it off its lines first.");
        }
    }

    /// <summary>
    /// The same section known by <paramref name="key"/>, with <paramref name="sectionLines"/>: a version's section put back,
    /// known by the sub-assembly it had there, as a line that is it names it.
    /// </summary>
    public ProductUsageEdit Keyed(long key, IReadOnlyList<UsedSectionEdit> sectionLines, bool isUnlocked) =>
        new(SubAssemblyId, Role, Lines, AuthoredField, AuthoredValue, LinkedName, AddIns, Name, _isOwn, _isReleasing, new KeptLines(_savedLines, LinesTaken), LinkedOwner, _toMadeIngredient)
        {
            Key = key,
            SectionLines = sectionLines,
            IsUnlocked = isUnlocked,
        };

    /// <param name="subAssemblyId">The sub-assembly's id, or 0 for a new section, not saved yet.</param>
    /// <param name="linkedName">The linked sub-assembly's name, e.g. "Cheese Roll Filling: Ube"; null for the product's own or a new section.</param>
    /// <param name="addIns">This product's own extra ingredients on top of a linked recipe; always empty for its own or a new section.</param>
    /// <param name="name">
    /// What it goes by beside its role: the optional name typed for a new section, or a linked one's name less this
    /// product's own and its role (SubAssemblyRules.SectionLabel), "Ube".
    /// </param>
    /// <param name="isOwn">
    /// A new section Save makes the product's own whatever its role: a linked one unlinked here. A saved one of the
    /// product's own is its own without it.
    /// </param>
    /// <param name="isReleasing">One of the product's own that Save moves to the Sub-Assemblies screen and links (Move to Sub-Assemblies).</param>
    /// <param name="toMadeIngredient">The made ingredient one of the product's own becomes on Save (Move to In-House); null for none.</param>
    /// <param name="kept">
    /// A linked one's lines as the Sub-Assemblies screen has them (<see cref="KeptLines"/>); null when they're
    /// <paramref name="lines"/>, with nothing changed here.
    /// </param>
    /// <param name="linkedOwner">The product a linked one is kept in, its own, which only a variant links; null for one on the Sub-Assemblies screen.</param>
    public ProductUsageEdit(
        int subAssemblyId,
        SubAssemblyRole? role,
        IReadOnlyList<SectionLine> lines,
        UsageAuthoredField authoredField,
        decimal authoredValue,
        string? linkedName,
        IReadOnlyList<SectionLine>? addIns = null,
        string? name = null,
        bool isOwn = false,
        bool isReleasing = false,
        KeptLines? kept = null,
        (int Id, string Name)? linkedOwner = null,
        MadeIngredientDraft? toMadeIngredient = null)
    {
        _isOwn = isOwn;
        _isReleasing = isReleasing;
        _toMadeIngredient = toMadeIngredient;
        _savedLines = kept?.Now ?? lines;
        LinesTaken = kept?.Taken ?? _savedLines;
        Name = SubAssemblyRules.NormalizeName(name);
        SubAssemblyId = subAssemblyId;
        Role = role;
        Lines = lines;
        AuthoredField = authoredField;
        AuthoredValue = authoredValue;
        LinkedName = linkedName;
        LinkedOwner = linkedName is null ? null : linkedOwner;
        AddIns = addIns ?? [];

        // Worked out at both costs, buffered and true, so the screen can switch between them without building its
        // sections again.
        Figures = SectionFigures.Of(lines, AddIns, authoredField, authoredValue);
    }

    /// <summary>
    /// The section's own batch and piece figures, at both costs: its lines and add-ins alone. The recipe's figures
    /// (ProductRecipeDraft.Priced) add its lines that are other sections, and leave its pieces what the others don't use of
    /// it; nothing else differs.
    /// </summary>
    public SectionFigures Figures { get; }

    /// <summary>
    /// The section as it stands now, called what it's called now, at its own figures: what ProductRecipeDraft.Priced prices
    /// with the recipe's others for the costing and printouts.
    /// </summary>
    public PricedSection ToPriced() =>
        new(SubAssemblyId, Role, Label, SectionName, LinkedName, LinkedOwner, AuthoredField, AuthoredValue, Lines, AddIns, Figures)
        {
            SectionLines = [.. SectionLines.Select(l => new SectionUse(l.Key > 0 ? (int)l.Key : 0, l.Grams, l.AuthoredBakersPercent, l.Position))],
            IsUnlocked = IsUnlocked,
        };

    public int SubAssemblyId { get; }

    public SubAssemblyRole? Role { get; }

    public int RoleId => Role?.Id ?? 0;

    public string RoleName => Role?.Name ?? string.Empty;

    /// <summary>
    /// What it goes by beside its role, e.g. "Ube": the name typed for a new section, which it needn't have, or a linked
    /// one's name less this product's own and its role, and nothing when that's all its name is (SubAssemblyRules.SectionLabel).
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// What it goes by beside its role when the recipe has more than one in the role: <see cref="Name"/>, which one in a
    /// role can go without.
    /// </summary>
    public string? Label => Name;

    private bool _isRoleRepeated;
    /// <summary>
    /// Whether the recipe has other sections in its role, so this one is called by its name as well. The screen sets it
    /// as the recipe changes (ProductRecipeDraft.MarkRepeatedRoles).
    /// </summary>
    public bool IsRoleRepeated
    {
        get => _isRoleRepeated;
        set
        {
            RaiseAndSetIfChanged(ref _isRoleRepeated, value);
            RaisePropertyChanged(nameof(SectionName));
            RaisePropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>
    /// What the section is called: its tab and its heading. Its role, e.g. "Filling",
    /// and with its name when the recipe has more than one in the role: "Filling: Ube" beside "Filling: Cheese".
    /// </summary>
    public string SectionName => PricedSection.NameOf(RoleName, Label, IsRoleRepeated);

    public IReadOnlyList<SectionLine> Lines { get; }

    /// <summary>
    /// This product's own extra ingredients on top of a recipe it links — a colouring, a flavouring, an
    /// inclusion. Empty for a new section, whose lines the form edits directly.
    /// </summary>
    public IReadOnlyList<SectionLine> AddIns { get; }

    /// <summary>Which figure the user typed; it stays put when the batch changes, and the other follows.</summary>
    public UsageAuthoredField AuthoredField { get; }

    public decimal AuthoredValue { get; }

    /// <summary>The linked sub-assembly's name, e.g. "Cheese Roll Filling: Ube"; null for the product's own or a new section.</summary>
    public string? LinkedName { get; }

    /// <summary>
    /// The product a linked one is kept in, as its own: a variant links its source's. Null for one on the Sub-Assemblies
    /// screen, or for a section that isn't linked.
    /// </summary>
    public (int Id, string Name)? LinkedOwner { get; }

    /// <summary>
    /// The form edits its lines: the product's own (<see cref="IsOwned"/>), or a new section, which Save makes the
    /// product's own in a product-owned role, and otherwise keeps on the Sub-Assemblies screen and links.
    /// </summary>
    public bool IsEditable => LinkedName is null;

    /// <summary>Not saved yet.</summary>
    public bool IsNew => SubAssemblyId == 0;

    private readonly bool _isOwn;

    /// <summary>
    /// The product's own sub-assembly: a saved one, made in a product-owned role (SubAssemblyRole.IsProductOwned) or
    /// unlinked, or a linked one unlinked here and not saved yet. Edited here, not on the Sub-Assemblies screen, and
    /// deleted when it's taken out of the recipe.
    /// </summary>
    public bool IsOwned => LinkedName is null && (SubAssemblyId > 0 || _isOwn);

    private readonly bool _isReleasing;

    /// <summary>
    /// One of the product's own that Save moves to the Sub-Assemblies screen, keeping its name and lines, and links
    /// (Move to Sub-Assemblies). Its lines are still edited here until then.
    /// </summary>
    public bool IsReleasing => IsOwned && _isReleasing;

    private readonly MadeIngredientDraft? _toMadeIngredient;

    /// <summary>
    /// The made ingredient Save makes from one of the product's own (Move to In-House): the section goes to the
    /// Sub-Assemblies screen, as Move to Sub-Assemblies takes it, this product links it there, and the made ingredient is
    /// made from it. Its lines are still edited here until then. Null for any other section.
    /// </summary>
    public MadeIngredientDraft? ToMadeIngredient => IsOwned ? _toMadeIngredient : null;

    public bool IsMovingToInHouse => ToMadeIngredient is not null;

    /// <summary>The badge a section moving to In-House shows: "Moves to Sub-Assemblies and In-House on Save, as Cheese Filling".</summary>
    public string MovingToInHouseText => ToMadeIngredient is { } made ? $"Moves to Sub-Assemblies and In-House on Save, as {made.Name}" : string.Empty;

    /// <summary>
    /// Move to In-House is offered: one of the product's own, not already moving to the Sub-Assemblies screen or to
    /// In-House, and not just one made ingredient already, which would only be wrapped in another.
    /// </summary>
    public bool CanMoveToInHouse =>
        IsOwned && !IsReleasing && !IsMovingToInHouse && Lines is not [{ Ingredient.Source: RawMaterialSource.Made }];

    public bool IsLinked => LinkedName is not null;

    /// <summary>
    /// A linked one's lines as the Sub-Assemblies screen has them, which Save compares against: the same as
    /// <see cref="Lines"/> until they're changed here.
    /// </summary>
    private readonly IReadOnlyList<SectionLine> _savedLines;

    /// <summary>
    /// A linked one's lines were changed in this product: Save writes them to the sub-assembly where it's kept, and every
    /// product linking it follows. Only what Save would write counts, not what the lines cost.
    /// </summary>
    public bool AreLinkedLinesEdited => IsLinked && !HaveSameRecipe(Lines, _savedLines);

    /// <summary>
    /// The lines a linked one's were changed from: what Save checks are still the ones kept there, so it never writes over
    /// others saved since (SavedVersionRules.EnsureLinesNotSavedSince). A load moves it only while nothing's changed here.
    /// </summary>
    public IReadOnlyList<SectionLine> LinesTaken { get; }

    /// <summary>Whether a linked one's lines are kept as <paramref name="other"/>'s are: a load that found others saved there makes them differ.</summary>
    internal bool HasSameKeptLines(ProductUsageEdit other) => !IsLinked || HaveSameRecipe(_savedLines, other._savedLines);

    /// <summary>The section form's title over it: "Edit Dough", a linked one with where it's kept.</summary>
    internal string EditTitle => IsEditable ? $"Edit {SectionName}" : $"Edit {SectionName} ({LinkedFromText})";

    /// <summary>
    /// The badge beside a linked section's heading, which opens where it's kept: "Cheese Roll Dough, from Sub-Assemblies",
    /// or a variant's "Ensaymada Filling, from Ensaymada", the product whose own it is.
    /// </summary>
    public string LinkedFromText => PricedSection.LinkedFromText(LinkedName ?? string.Empty, LinkedOwner);

    /// <summary>The badge's tooltip: where it's kept, and that Save here writes changed lines there.</summary>
    public string LinkedFromToolTip => LinkedOwner is { } owner
        ? $"{owner.Name}'s own, linked here as its variant. Save writes line changes there. Click to open {owner.Name}."
        : "Kept on Sub-Assemblies and shared by every product linking it. Save writes line changes there. Click to open it.";

    /// <summary>What it's called where the recipe it's in isn't plain: a linked one by its name, a new one as its section is.</summary>
    public string DisplayName => LinkedName ?? SectionName;

    // Its own figures, without its lines that are other sections or what the others use of it (Figures): the recipe's are
    // ProductRecipeDraft.FiguresOf's.

    /// <summary>The sub-assembly's own batch, before this product's add-ins: what the form's caption compares against.</summary>
    public decimal BaseTotalWeight => Figures.BaseTotalWeight;

    /// <summary>The batch a piece is cut from: the sub-assembly's own plus this product's add-ins.</summary>
    public decimal TotalWeight => Figures.TotalWeight;

    /// <summary>What the whole batch costs, add-ins included, at <paramref name="basis"/>.</summary>
    public decimal TotalCostAt(CostBasis basis) => Figures.TotalCostAt(basis);

    /// <summary>Grams of the batch in one piece.</summary>
    public decimal WeightPerPiece => Figures.WeightPerPiece;

    /// <summary>Pieces one batch makes. Expected to differ across a product's sub-assemblies.</summary>
    public decimal Yield => Figures.Yield;

    /// <summary>What one piece costs at <paramref name="basis"/>; 0 while the batch weighs nothing or has no piece size.</summary>
    public decimal CostPerPieceAt(CostBasis basis) => Figures.CostPerPieceAt(basis);

    /// <summary>A new section with its role refreshed (renamed in Settings) and its lines repriced with their ingredients.</summary>
    public ProductUsageEdit With(SubAssemblyRole? role, IReadOnlyList<SectionLine> lines) =>
        new(SubAssemblyId, role, lines, AuthoredField, AuthoredValue, LinkedName, AddIns, Name, _isOwn, _isReleasing, linkedOwner: LinkedOwner, toMadeIngredient: _toMadeIngredient)
        {
            Key = Key,
            SectionLines = SectionLines,
            IsUnlocked = IsUnlocked,
        };

    /// <summary>
    /// The same section with each ingredient of its lines, its add-ins and a linked one's lines as kept swapped for
    /// <paramref name="reprice"/>'s copy, and nothing else: what Save writes, the piece size and the name stay. Itself when
    /// every ingredient is the copy it holds already.
    /// </summary>
    public ProductUsageEdit WithIngredients(Func<IReadOnlyList<SectionLine>, IReadOnlyList<SectionLine>> reprice)
    {
        var lines = reprice(Lines);
        var addIns = reprice(AddIns);
        var savedLines = ReferenceEquals(_savedLines, Lines) ? lines : reprice(_savedLines);
        return lines.SequenceEqual(Lines) && addIns.SequenceEqual(AddIns) && savedLines.SequenceEqual(_savedLines)
            ? this
            : new(SubAssemblyId, Role, lines, AuthoredField, AuthoredValue, LinkedName, addIns, Name, _isOwn, _isReleasing, new KeptLines(savedLines, LinesTaken), LinkedOwner, _toMadeIngredient)
            {
                Key = Key,
                SectionLines = SectionLines,
                IsUnlocked = IsUnlocked,
            };
    }

    /// <summary>The same section of the product's own, for Save to move to the Sub-Assemblies screen and link.</summary>
    public ProductUsageEdit Released() =>
        new(SubAssemblyId, Role, Lines, AuthoredField, AuthoredValue, LinkedName, AddIns, Name, _isOwn, isReleasing: true, linkedOwner: LinkedOwner)
        {
            Key = Key,
            SectionLines = SectionLines,
            IsUnlocked = IsUnlocked,
        };

    /// <summary>The same section of the product's own, for Save to turn into <paramref name="made"/> (Move to In-House).</summary>
    public ProductUsageEdit MovedToInHouse(MadeIngredientDraft made) =>
        new(SubAssemblyId, Role, Lines, AuthoredField, AuthoredValue, LinkedName, AddIns, Name, _isOwn, linkedOwner: LinkedOwner, toMadeIngredient: made)
        {
            Key = Key,
            SectionLines = SectionLines,
            IsUnlocked = IsUnlocked,
        };

    /// <summary>
    /// A linked section as the Sub-Assemblies screen now has it — its name, the label it gives (<paramref name="name"/>),
    /// its role and <paramref name="savedLines"/> — with its add-ins repriced; its piece size stays the product's. Lines
    /// changed here and not saved yet stay as they are, repriced (<paramref name="editedLines"/>), and so do the lines they
    /// were changed from (<see cref="LinesTaken"/>), as they do while some are typed in its form and not applied
    /// (<paramref name="areLinesTypedInto"/>): Save refuses to write them over others saved there since.
    /// </summary>
    public ProductUsageEdit WithLinked(
        string linkedName,
        string? name,
        SubAssemblyRole? role,
        IReadOnlyList<SectionLine> savedLines,
        IReadOnlyList<SectionLine>? editedLines,
        IReadOnlyList<SectionLine> addIns,
        (int Id, string Name)? linkedOwner,
        bool areLinesTypedInto)
    {
        var lines = editedLines ?? savedLines;
        var taken = areLinesTypedInto || !HaveSameRecipe(lines, savedLines) ? LinesTaken : savedLines;
        return new(SubAssemblyId, role, lines, AuthoredField, AuthoredValue, linkedName, addIns, name, kept: new KeptLines(savedLines, taken), linkedOwner: linkedOwner)
        {
            Key = Key,
            SectionLines = SectionLines,
            IsUnlocked = IsUnlocked,
        };
    }

    /// <summary>The same linked section with the lines, piece size and add-ins the form now holds, and its lines that are other sections.</summary>
    public ProductUsageEdit WithLinkedForm(
        IReadOnlyList<SectionLine> lines,
        UsageAuthoredField authoredField,
        decimal authoredValue,
        IReadOnlyList<SectionLine> addIns,
        IReadOnlyList<UsedSectionEdit> sectionLines,
        bool isUnlocked) =>
        new(SubAssemblyId, Role, lines, authoredField, authoredValue, LinkedName, addIns, Name, kept: new KeptLines(_savedLines, LinesTaken), linkedOwner: LinkedOwner)
        {
            Key = Key,
            SectionLines = sectionLines,
            IsUnlocked = isUnlocked,
        };

    /// <summary>Whether two sets of lines would save the same: the same ingredients, grams and base ticks, in the same order, whatever they cost.</summary>
    private static bool HaveSameRecipe(IReadOnlyList<SectionLine> a, IReadOnlyList<SectionLine> b) =>
        ReferenceEquals(a, b)
        || (a.Count == b.Count
            && a.Zip(b).All(pair =>
                pair.First.Ingredient.Id == pair.Second.Ingredient.Id
                && pair.First.Grams == pair.Second.Grams
                && pair.First.IsFormulaBase == pair.Second.IsFormulaBase
                && pair.First.AuthoredBakersPercent == pair.Second.AuthoredBakersPercent));
}
