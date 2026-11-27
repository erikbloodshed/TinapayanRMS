using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261127001030_CategoryCostingAndMarkupFloor : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "administrative_percent",
            table: "product_categories",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "labor_percent",
            table: "product_categories",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "markup_floor_percent",
            table: "product_categories",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "markup_percent",
            table: "product_categories",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "overhead_percent",
            table: "product_categories",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "markup_floor_percent",
            table: "costing_defaults",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_ProductCategories_CostingPercents",
            table: "product_categories",
            sql: "(labor_percent IS NULL OR labor_percent >= 0) AND (overhead_percent IS NULL OR overhead_percent >= 0) AND (administrative_percent IS NULL OR administrative_percent >= 0) AND (markup_percent IS NULL OR markup_percent >= 0) AND (markup_floor_percent IS NULL OR markup_floor_percent >= 0)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_CostingDefaults_MarkupFloorPercent",
            table: "costing_defaults",
            sql: "markup_floor_percent IS NULL OR markup_floor_percent >= 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_ProductCategories_CostingPercents",
            table: "product_categories");

        migrationBuilder.DropCheckConstraint(
            name: "CK_CostingDefaults_MarkupFloorPercent",
            table: "costing_defaults");

        migrationBuilder.DropColumn(
            name: "administrative_percent",
            table: "product_categories");

        migrationBuilder.DropColumn(
            name: "labor_percent",
            table: "product_categories");

        migrationBuilder.DropColumn(
            name: "markup_floor_percent",
            table: "product_categories");

        migrationBuilder.DropColumn(
            name: "markup_percent",
            table: "product_categories");

        migrationBuilder.DropColumn(
            name: "overhead_percent",
            table: "product_categories");

        migrationBuilder.DropColumn(
            name: "markup_floor_percent",
            table: "costing_defaults");
    }
}
