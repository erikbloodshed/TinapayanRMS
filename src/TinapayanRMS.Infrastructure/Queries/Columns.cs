using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Queries;

/// <summary>
/// The columns every Dapper read selects for each entity, id first so a multi-mapped join can split on it. They're the
/// snake_case names EF gives the columns, which Dapper maps back to the PascalCase properties (DapperReads).
/// They must match EF's model exactly: a column left out here reads back as its default without any error.
/// ColumnsTests checks them against the model, so a new or renamed property fails that test until it's listed.
/// </summary>
internal static class Columns
{
    internal static readonly IReadOnlyDictionary<Type, string[]> ByEntity = new Dictionary<Type, string[]>
    {
        [typeof(UnitOfMeasure)] = ["id", "name", "abbreviation"],
        [typeof(RawMaterialCategory)] = ["id", "name", "type"],
        [typeof(RawMaterial)] =
        [
            "id", "name", "type", "category_id", "source", "weight_loss_percent", "linked_to_id", "weight_share_percent",
            "cost_share_percent", "base_unit_id", "cost_per_base_unit", "cost_buffer_percent", "buffered_cost_per_base_unit",
            "updated_at", "cost_updated_at", "sub_assembly_id", "target_cost_buffer_percent", "preferred_brand",
        ],
        [typeof(RawMaterialUnit)] = ["id", "raw_material_id", "unit_of_measure_id", "base_units_per_unit", "description"],
        [typeof(RawMaterialBrandPrice)] = ["id", "raw_material_id", "brand", "unit_id", "price"],
        [typeof(RawMaterialPriceHistory)] = ["id", "raw_material_id", "date", "unit_id", "brand", "price"],
        [typeof(IngredientAddInLine)] = ["id", "ingredient_id", "add_in_ingredient_id", "grams", "sort_order"],
        [typeof(SubAssemblyRole)] = ["id", "name", "sort_order", "is_packaging", "is_product_owned"],
        [typeof(SubAssembly)] = ["id", "name", "role_id", "owner_product_id", "total_weight", "total_cost"],
        [typeof(SubAssemblyLine)] = ["id", "sub_assembly_id", "ingredient_id", "grams", "is_formula_base", "authored_bakers_percent", "sort_order"],
        [typeof(SubAssemblyPackagingLine)] = ["id", "sub_assembly_id", "packaging_material_id", "unit_id", "qty_per_set", "sort_order"],
        [typeof(ProductCategory)] = ["id", "name", "wastage_percent"],
        [typeof(Product)] = ["id", "name", "description", "category_id", "quantity_required", "updated_at", "current_price", "stage", "is_composite", "includes_component_packaging", "deleted_at"],
        [typeof(ProductComponent)] = ["id", "product_id", "component_product_id", "quantity", "sort_order"],
        [typeof(ProductSubAssemblyUsage)] = ["id", "product_id", "sub_assembly_id", "authored_field", "authored_value"],
        [typeof(UsageAddInLine)] = ["id", "usage_id", "ingredient_id", "grams", "sort_order"],
        [typeof(PackagingLine)] =
            ["id", "product_id", "packaging_material_id", "unit_id", "qty_per_unit", "units_per_packaging_unit", "authored_field", "sort_order"],
        [typeof(CostingParameters)] =
            ["id", "product_id", "labor_percent", "overhead_percent", "administrative_percent", "margin_percent", "vat_percent"],
        [typeof(CostingDefaults)] = ["id", "labor_percent", "overhead_percent", "administrative_percent", "margin_percent", "vat_percent", "wastage_percent", "new_product_stage", "cost_buffer_percent", "price_rounding_step"],
        [typeof(PackagingLink)] = ["id", "product_id", "linked_product_id", "linked_sub_assembly_id"],
        [typeof(ProductVersion)] = ["id", "product_id", "number", "saved_at", "note", "content"],
        [typeof(PriceScenario)] = ["id", "name", "saved_at"],
        [typeof(PriceScenarioLine)] = ["id", "price_scenario_id", "raw_material_id", "change_percent", "new_price", "sort_order"],
        [typeof(SavedPlan)] = ["id", "name", "saved_at", "half_batches"],
        [typeof(SavedPlanLine)] = ["id", "saved_plan_id", "product_id", "units", "sort_order"],
    };

    /// <summary><typeparamref name="T"/>'s columns qualified by <paramref name="alias"/>, for a SELECT list.</summary>
    public static string Of<T>(string alias) => string.Join(", ", ByEntity[typeof(T)].Select(c => $"{alias}.{c}"));
}
