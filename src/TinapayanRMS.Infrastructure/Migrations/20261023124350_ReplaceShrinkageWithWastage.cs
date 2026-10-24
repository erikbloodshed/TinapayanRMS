using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceShrinkageWithWastage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductSubAssemblyUsages_ShrinkagePercent",
                table: "product_sub_assembly_usages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CostingDefaults_ShrinkagePercent",
                table: "costing_defaults");

            migrationBuilder.DropColumn(
                name: "shrinkage_percent",
                table: "product_sub_assembly_usages");

            migrationBuilder.DropColumn(
                name: "shrinkage_percent",
                table: "costing_defaults");

            migrationBuilder.AddColumn<decimal>(
                name: "wastage_percent",
                table: "costing_defaults",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 3m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CostingDefaults_WastagePercent",
                table: "costing_defaults",
                sql: "wastage_percent >= 0 AND wastage_percent <= 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CostingDefaults_WastagePercent",
                table: "costing_defaults");

            migrationBuilder.DropColumn(
                name: "wastage_percent",
                table: "costing_defaults");

            migrationBuilder.AddColumn<decimal>(
                name: "shrinkage_percent",
                table: "product_sub_assembly_usages",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "shrinkage_percent",
                table: "costing_defaults",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductSubAssemblyUsages_ShrinkagePercent",
                table: "product_sub_assembly_usages",
                sql: "shrinkage_percent >= 0 AND shrinkage_percent < 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CostingDefaults_ShrinkagePercent",
                table: "costing_defaults",
                sql: "shrinkage_percent >= 0 AND shrinkage_percent < 100");
        }
    }
}
