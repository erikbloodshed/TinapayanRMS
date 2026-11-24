using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing;

/// <summary>
/// One section of a product's recipe, read and priced: what a saved product's readers (Reports, Print, the Price
/// Simulator, the Recipe API and the MCP server) see. Immutable; the Products screen's editable section is
/// ProductUsageEdit, which gives one of these as it stands (ProductUsageEdit.ToPriced).
/// </summary>
/// <param name="SubAssemblyId">The sub-assembly's id, or 0 for a section not saved yet.</param>
/// <param name="Label">What it goes by beside its role, "Ube"; null when it goes by its role alone.</param>
/// <param name="SectionName">What the recipe calls it: its role, and its label too when the recipe has more than one section in that role.</param>
/// <param name="LinkedName">The linked sub-assembly's name; null for the product's own or a new section.</param>
/// <param name="LinkedOwner">The product a linked one is kept in as its own (a variant's link); null for one on the Sub-Assemblies screen.</param>
public sealed record PricedSection(
    int SubAssemblyId,
    SubAssemblyRole? Role,
    string? Label,
    string SectionName,
    string? LinkedName,
    (int Id, string Name)? LinkedOwner,
    UsageAuthoredField AuthoredField,
    decimal AuthoredValue,
    IReadOnlyList<SectionLine> Lines,
    IReadOnlyList<SectionLine> AddIns,
    SectionFigures Figures)
{
    /// <summary>Where every sub-assembly not kept in a product is kept, and edited: the Sub-Assemblies screen.</summary>
    public const string StandaloneSourceName = "Sub-Assemblies";

    /// <summary>Sections in recipe order: by their role's sort order, then in the order they were saved, then any not saved yet.</summary>
    public static IComparer<PricedSection> RecipeOrder { get; } =
        Comparer<PricedSection>.Create((a, b) => CompareInRecipe(a.Role, a.SubAssemblyId, b.Role, b.SubAssemblyId));

    public string RoleName => Role?.Name ?? string.Empty;

    /// <summary>
    /// Its lines that are the product's other sections (UsageSectionLine), in its table's order: among its lines, or a linked
    /// one's add-ins, each at its <see cref="SectionUse.Position"/>. Named and priced by <see cref="SectionUses"/>.
    /// </summary>
    public IReadOnlyList<SectionUse> SectionLines { get; init; } = [];

    /// <summary>Some of its batch is on the product's other sections' lines: its pieces come of what's left.</summary>
    public bool IsUsedElsewhere => Figures.UsedElsewhereWeight > 0;

    /// <summary>
    /// It costs the product nothing per piece where it should cost something: it has no weight per piece or yield, and no
    /// other section uses it either, or it has a yield and the others use the whole of it. The Products list, Reports and
    /// Print flag it. One with no piece size that another uses is that one's ingredient, and costs through it.
    /// </summary>
    public bool LacksPieceSize => HasPieceSize ? IsUsedElsewhere && WeightPerPiece <= 0 : !IsUsedElsewhere;

    public bool IsLinked => LinkedName is not null;

    /// <summary>Where a linked one is kept, "Cheese Roll Dough, from Sub-Assemblies"; null for the product's own or a new section.</summary>
    public string? LinkedFrom => LinkedName is null ? null : LinkedFromText(LinkedName, LinkedOwner);

    public decimal TotalWeight => Figures.TotalWeight;

    public decimal WeightPerPiece => Figures.WeightPerPiece;

    public decimal Yield => Figures.Yield;

    /// <summary>
    /// Whether it has a weight per piece or a yield (ProductRules.HasPieceSize): without one it costs the product nothing per
    /// piece, whatever its batch costs.
    /// </summary>
    public bool HasPieceSize => ProductRules.HasPieceSize(AuthoredValue);

    public decimal TotalCostAt(CostBasis basis) => Figures.TotalCostAt(basis);

    public decimal CostPerPieceAt(CostBasis basis) => Figures.CostPerPieceAt(basis);

    /// <summary>A section's name: its role, "Filling", or "Filling: Ube" when the recipe has more than one in the role and it has a label.</summary>
    public static string NameOf(string roleName, string? label, bool isRoleRepeated) =>
        isRoleRepeated && label is not null ? $"{roleName}: {label}" : roleName;

    /// <summary>"Cheese Roll Dough, from Sub-Assemblies", or a variant's "Ensaymada Filling, from Ensaymada".</summary>
    public static string LinkedFromText(string linkedName, (int Id, string Name)? linkedOwner) =>
        $"{linkedName}, from {linkedOwner?.Name ?? StandaloneSourceName}";

    /// <summary>
    /// Recipe order for two sections, by role and saved id: role sort order (a section with no role last), then role name,
    /// then the order they were saved in, with one not saved yet (id 0) after every saved one.
    /// </summary>
    public static int CompareInRecipe(SubAssemblyRole? roleA, int subAssemblyIdA, SubAssemblyRole? roleB, int subAssemblyIdB)
    {
        var byRole = (roleA?.SortOrder ?? int.MaxValue).CompareTo(roleB?.SortOrder ?? int.MaxValue);
        if (byRole == 0)
        {
            byRole = StringComparer.CurrentCultureIgnoreCase.Compare(roleA?.Name ?? string.Empty, roleB?.Name ?? string.Empty);
        }

        return byRole != 0 ? byRole : SavedOrder(subAssemblyIdA).CompareTo(SavedOrder(subAssemblyIdB));

        static int SavedOrder(int subAssemblyId) => subAssemblyId > 0 ? subAssemblyId : int.MaxValue;
    }

    /// <summary>
    /// A saved usage of <paramref name="productName"/>'s as its section: the sub-assembly's lines, the product's add-ins and
    /// lines that are its other sections, and the piece size, named after <paramref name="role"/>. Its figures are its own
    /// batch's, until <see cref="SectionUses"/> prices the lines that are sections. <paramref name="ingredientsById"/>, when given, replaces the
    /// ingredients the lines were read with (the Price Simulator's materials at other costs, or a screen's own copies).
    /// </summary>
    public static PricedSection FromSaved(
        ProductSubAssemblyUsage usage,
        IEnumerable<SubAssemblyLine> lines,
        IEnumerable<UsageAddInLine> addIns,
        IEnumerable<UsageSectionLine> sectionUses,
        SubAssemblyRole? role,
        string productName,
        bool isRoleRepeated,
        IReadOnlyDictionary<int, RawMaterial>? ingredientsById = null)
    {
        var subAssembly = usage.SubAssembly!;
        var isOwn = subAssembly.OwnerProductId == usage.ProductId;
        RawMaterial IngredientOf(int id, RawMaterial? read) => ingredientsById?.GetValueOrDefault(id) ?? read!;
        IReadOnlyList<SectionLine> sectionLines =
            [.. lines.Select(l => new SectionLine(IngredientOf(l.IngredientId, l.Ingredient), l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];
        IReadOnlyList<SectionLine> sectionAddIns =
            [.. addIns.Select(a => new SectionLine(IngredientOf(a.IngredientId, a.Ingredient), a.Grams))];
        var roleName = role?.Name ?? string.Empty;
        var label = SubAssemblyRules.NormalizeName(SubAssemblyRules.LabelIn(subAssembly, usage.ProductId, productName, roleName));
        return new PricedSection(
            subAssembly.Id,
            role,
            label,
            NameOf(roleName, label, isRoleRepeated),
            isOwn ? null : subAssembly.Name,
            isOwn ? null : LinkedOwnerOf(subAssembly),
            usage.AuthoredField,
            usage.AuthoredValue,
            sectionLines,
            sectionAddIns,
            SectionFigures.Of(sectionLines, sectionAddIns, usage.AuthoredField, usage.AuthoredValue))
        {
            SectionLines = [.. sectionUses.Select(l => new SectionUse(l.SectionSubAssemblyId, l.Grams, l.AuthoredBakersPercent, l.Position))],
        };
    }

    /// <summary>The product a linked sub-assembly is kept in, when it's that product's own (a variant's link); null for one on the Sub-Assemblies screen.</summary>
    public static (int Id, string Name)? LinkedOwnerOf(SubAssembly subAssembly) =>
        subAssembly.OwnerProductId is { } ownerId ? (ownerId, subAssembly.OwnerProduct?.Name ?? $"Product {ownerId}") : null;

    /// <summary>The roles more than one of <paramref name="roleIds"/> is in: a section in one of them is called by its label too.</summary>
    public static IReadOnlySet<int> RepeatedRoles(IEnumerable<int> roleIds) =>
        roleIds.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
}

/// <summary>
/// A line of a section that is another of the product's sections (UsageSectionLine): the sponge on the dough's lines, by the
/// sponge's sub-assembly (0 for a section not saved yet), in grams, at its place among the section's lines. Named and priced
/// by <see cref="SectionUses"/>: it costs its grams at that section's cost per gram.
/// </summary>
/// <param name="AuthoredBakersPercent">The percentage typed for it, or null when its grams were.</param>
/// <param name="Position">Its place in its section's table, among the ingredient lines, or a linked one's add-ins.</param>
public sealed record SectionUse(int SubAssemblyId, decimal Grams, decimal? AuthoredBakersPercent, int Position)
{
    /// <summary>What the recipe calls the section it uses, "Sponge".</summary>
    public string SectionName { get; init; } = string.Empty;

    /// <summary>The whole batch of the section it uses, its weight and its costs: what its grams are a share of.</summary>
    public SectionsBatch UsedBatch { get; init; }

    /// <summary>What a gram of the section it uses costs at <paramref name="basis"/>; 0 while that weighs nothing.</summary>
    public decimal CostPerGramAt(CostBasis basis) => UsedBatch.Weight > 0 ? UsedBatch.CostAt(basis) / UsedBatch.Weight : 0m;

    /// <summary>
    /// What the line adds to its section's batch cost at <paramref name="basis"/>: its share of the batch it uses, multiplied
    /// before it's divided, so the whole batch costs what it does to the last digit.
    /// </summary>
    public decimal CostAt(CostBasis basis) => UsedBatch.Weight > 0 ? Grams * UsedBatch.CostAt(basis) / UsedBatch.Weight : 0m;
}
