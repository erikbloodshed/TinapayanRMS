using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261103142633_QuantityAndPercentChecks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "CK_UsageAddInLines_Grams",
            table: "usage_add_in_lines",
            sql: "grams > 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_SubAssemblyLines_Grams",
            table: "sub_assembly_lines",
            sql: "grams > 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_RawMaterialUnits_BaseUnitsPerUnit",
            table: "raw_material_units",
            sql: "base_units_per_unit > 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_PackagingLines_AuthoredQuantity",
            table: "packaging_lines",
            sql: "(authored_field = 0 AND qty_per_unit > 0) OR (authored_field = 1 AND units_per_packaging_unit > 0)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_IngredientAddInLines_Grams",
            table: "ingredient_add_in_lines",
            sql: "grams > 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_CostingParameters_Percents",
            table: "costing_parameters",
            sql: "labor_percent >= 0 AND overhead_percent >= 0 AND administrative_percent >= 0 AND margin_percent >= 0 AND vat_percent >= 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_UsageAddInLines_Grams",
            table: "usage_add_in_lines");

        migrationBuilder.DropCheckConstraint(
            name: "CK_SubAssemblyLines_Grams",
            table: "sub_assembly_lines");

        migrationBuilder.DropCheckConstraint(
            name: "CK_RawMaterialUnits_BaseUnitsPerUnit",
            table: "raw_material_units");

        migrationBuilder.DropCheckConstraint(
            name: "CK_PackagingLines_AuthoredQuantity",
            table: "packaging_lines");

        migrationBuilder.DropCheckConstraint(
            name: "CK_IngredientAddInLines_Grams",
            table: "ingredient_add_in_lines");

        migrationBuilder.DropCheckConstraint(
            name: "CK_CostingParameters_Percents",
            table: "costing_parameters");
    }
}
