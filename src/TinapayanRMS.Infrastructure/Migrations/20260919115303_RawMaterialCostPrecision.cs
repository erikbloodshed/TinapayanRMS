using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RawMaterialCostPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "CostPerBaseUnit",
                table: "RawMaterials",
                type: "decimal(28,20)",
                precision: 28,
                scale: 20,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "BufferedCostPerBaseUnit",
                table: "RawMaterials",
                type: "decimal(28,20)",
                precision: 28,
                scale: 20,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "CostPerBaseUnit",
                table: "RawMaterials",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,20)",
                oldPrecision: 28,
                oldScale: 20);

            migrationBuilder.AlterColumn<decimal>(
                name: "BufferedCostPerBaseUnit",
                table: "RawMaterials",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,20)",
                oldPrecision: 28,
                oldScale: 20);
        }
    }
}
