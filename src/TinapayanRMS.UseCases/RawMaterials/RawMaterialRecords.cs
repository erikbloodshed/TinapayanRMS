using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UseCases.RawMaterials;

/// <summary>
/// What the Raw Materials screen reads to show its list and fill its forms' pickers, read together: the categories, the
/// units of measure, every material, Settings' costing defaults, the standalone sub-assemblies a made ingredient can be made
/// from, and the products using each material whose details show.
/// </summary>
public sealed record RawMaterialsCatalog(
    IReadOnlyList<RawMaterialCategory> Categories,
    IReadOnlyList<UnitOfMeasure> UnitsOfMeasure,
    IReadOnlyList<RawMaterial> RawMaterials,
    CostingDefaults CostingDefaults,
    IReadOnlyList<SubAssembly> SubAssemblies,
    IReadOnlyDictionary<int, IReadOnlyList<LinkingProduct>> Dependents);

/// <summary>
/// What a material shows beyond the list's copy of it: a purchased one's price history and brand prices, a made one's BOM
/// lines, and the products using any of them.
/// </summary>
public sealed record RawMaterialDetail(
    IReadOnlyList<RawMaterialPriceHistory> PriceHistory,
    IReadOnlyList<RawMaterialBrandPrice> BrandPrices,
    IReadOnlyList<IngredientBomLine> BomLines,
    IReadOnlyList<LinkingProduct> Dependents)
{
    /// <summary>A new material's: nothing.</summary>
    public static readonly RawMaterialDetail None = new([], [], [], []);
}

/// <summary>
/// What a Raw Materials form's Save writes, all or nothing: the material (with when it had last been saved as the form took
/// it, which a save made since by another PC refuses it over), its alternative units, a purchased one's brand prices and a
/// made one's add-ins, as the form has them.
/// </summary>
public sealed record RawMaterialSaveRequest(
    RawMaterial Material,
    IReadOnlyList<RawMaterialUnit> Units,
    IReadOnlyList<(string? Brand, int? UnitOfMeasureId, decimal Price)>? BrandPrices,
    IReadOnlyList<(int IngredientId, decimal Grams)>? AddIns);
