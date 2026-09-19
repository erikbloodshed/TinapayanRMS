using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Queries;

/// <summary>
/// The columns every Dapper read selects for each entity, Id first so a multi-mapped join can split on it.
/// They must match EF's model exactly: a column left out here reads back as its default without any error.
/// ColumnsTests checks them against the model, so a new or renamed property fails that test until it's listed.
/// </summary>
internal static class Columns
{
    internal static readonly IReadOnlyDictionary<Type, string[]> ByEntity = new Dictionary<Type, string[]>
    {
        [typeof(UnitOfMeasure)] = ["Id", "Name", "Abbreviation"],
        [typeof(RawMaterialCategory)] = ["Id", "Name", "Type"],
        [typeof(RawMaterial)] =
        [
            "Id", "Name", "Type", "CategoryId", "Source", "WeightLossPercent", "LinkedToId", "WeightSharePercent",
            "CostSharePercent", "BaseUnitId", "CostPerBaseUnit", "CostBufferPercent", "BufferedCostPerBaseUnit",
        ],
        [typeof(RawMaterialUnit)] = ["Id", "RawMaterialId", "UnitOfMeasureId", "BaseUnitsPerUnit", "Description"],
        [typeof(RawMaterialBrandPrice)] = ["Id", "RawMaterialId", "Brand", "UnitId", "Price"],
        [typeof(RawMaterialPriceHistory)] = ["Id", "RawMaterialId", "Date", "UnitId", "Brand", "Price"],
        [typeof(IngredientBomLine)] = ["Id", "IngredientId", "ComponentIngredientId", "Grams"],
        [typeof(SubAssemblyRole)] = ["Id", "Name", "SortOrder", "IsPackaging"],
        [typeof(SubAssembly)] = ["Id", "Name", "RoleId", "OwnerProductId", "TotalWeight", "TotalCost"],
        [typeof(SubAssemblyLine)] = ["Id", "SubAssemblyId", "IngredientId", "Grams", "IsFormulaBase", "AuthoredBakersPercent"],
        [typeof(SubAssemblyPackagingLine)] = ["Id", "SubAssemblyId", "PackagingMaterialId", "QtyPerSet"],
        [typeof(ProductCategory)] = ["Id", "Name"],
        [typeof(Product)] = ["Id", "Name", "Description", "CategoryId", "UpdatedAt"],
        [typeof(ProductPack)] = ["Id", "ProductId", "Name", "PiecesPerPack"],
        [typeof(ProductSubAssemblyUsage)] = ["Id", "ProductId", "SubAssemblyId", "AuthoredField", "AuthoredValue"],
        [typeof(UsageAddInLine)] = ["Id", "UsageId", "IngredientId", "Grams"],
        [typeof(PackagingLine)] =
            ["Id", "ProductId", "PackId", "PackagingMaterialId", "UnitId", "QtyPerUnit", "UnitsPerPackagingUnit", "AuthoredField"],
        [typeof(CostingParameters)] =
            ["Id", "ProductId", "LaborPercent", "OverheadPercent", "AdministrativePercent", "MarginPercent", "VatPercent"],
        [typeof(PackagingLink)] = ["Id", "ProductId", "LinkedProductId", "LinkedSubAssemblyId"],
    };

    /// <summary><typeparamref name="T"/>'s columns qualified by <paramref name="alias"/>, for a SELECT list.</summary>
    public static string Of<T>(string alias) => string.Join(", ", ByEntity[typeof(T)].Select(c => $"{alias}.{c}"));
}
