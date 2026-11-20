using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UseCases.SubAssemblies;

/// <summary>
/// What the Sub-Assemblies screen reads to show its list and fill its forms' pickers, read together: the roles, every raw
/// material, the standalone sub-assemblies, the products linking each, and the made ingredients made from each.
/// </summary>
public sealed record SubAssembliesCatalog(
    IReadOnlyList<SubAssemblyRole> Roles,
    IReadOnlyList<RawMaterial> RawMaterials,
    IReadOnlyList<SubAssembly> SubAssemblies,
    IReadOnlyDictionary<int, IReadOnlyList<LinkingProduct>> LinkedBy,
    IReadOnlyDictionary<int, IReadOnlyList<MadeIngredientUse>> MadeFrom);

/// <summary>A sub-assembly's lines as saved: a recipe's ingredient lines, or a packaging set's lines, the other one empty.</summary>
public sealed record SubAssemblyContent(IReadOnlyList<SubAssemblyLine> Lines, IReadOnlyList<SubAssemblyPackagingLine> PackagingLines);

/// <summary>One of a recipe's lines as a Save writes it: the ingredient as the screen priced it, and how much goes in.</summary>
/// <param name="AuthoredBakersPercent">The percentage typed for the line, or null when its grams were.</param>
public sealed record IngredientLineDraft(RawMaterial Ingredient, decimal Grams, bool IsFormulaBase, decimal? AuthoredBakersPercent);

/// <summary>One of a packaging set's lines as a Save writes it: how much of the material one set takes.</summary>
/// <param name="UnitId">One of the material's alternative units, or null for its base unit.</param>
public sealed record PackagingLineDraft(int PackagingMaterialId, int? UnitId, decimal QtyPerSet);

/// <summary>
/// What a Sub-Assemblies form's Save writes: the sub-assembly (0 for a new one), its name and role, and its lines, a
/// recipe's or a packaging set's as its role says, with when it had last been saved as the form took it, which a save made
/// since by another PC refuses it over (SavedVersionRules).
/// </summary>
public sealed record SubAssemblySaveRequest(
    int Id,
    string Name,
    int RoleId,
    bool IsPackagingSet,
    DateTime? SavedVersion,
    IReadOnlyList<IngredientLineDraft> Lines,
    IReadOnlyList<PackagingLineDraft> PackagingLines);

/// <summary>A product whose base cost a sub-assembly's new lines move, for one sellable unit.</summary>
public sealed record RecipeImpact(string ProductName, decimal BaseCostBefore, decimal BaseCostAfter)
{
    public decimal Change => BaseCostAfter - BaseCostBefore;
}
