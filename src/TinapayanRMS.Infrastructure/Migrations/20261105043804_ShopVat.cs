using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261105043804_ShopVat : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_CostingParameters_Percents",
            table: "costing_parameters");

        migrationBuilder.DropColumn(
            name: "vat_percent",
            table: "costing_parameters");

        migrationBuilder.AddCheckConstraint(
            name: "CK_CostingParameters_Percents",
            table: "costing_parameters",
            sql: "labor_percent >= 0 AND overhead_percent >= 0 AND administrative_percent >= 0 AND margin_percent >= 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_CostingDefaults_VatPercent",
            table: "costing_defaults",
            sql: "vat_percent >= 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_CostingParameters_Percents",
            table: "costing_parameters");

        migrationBuilder.DropCheckConstraint(
            name: "CK_CostingDefaults_VatPercent",
            table: "costing_defaults");

        migrationBuilder.AddColumn<decimal>(
            name: "vat_percent",
            table: "costing_parameters",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddCheckConstraint(
            name: "CK_CostingParameters_Percents",
            table: "costing_parameters",
            sql: "labor_percent >= 0 AND overhead_percent >= 0 AND administrative_percent >= 0 AND margin_percent >= 0 AND vat_percent >= 0");
    }
}
