using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCostBuffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BufferedCostPerBaseUnit",
                table: "RawMaterials",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CostBufferPercent",
                table: "RawMaterials",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            // Added by hand: every material starts with no buffer, so its buffered cost is its cost.
            migrationBuilder.Sql("UPDATE [RawMaterials] SET [BufferedCostPerBaseUnit] = [CostPerBaseUnit];");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RawMaterials_CostBufferPercent",
                table: "RawMaterials",
                sql: "[CostBufferPercent] >= 0 AND [CostBufferPercent] <= 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RawMaterials_CostBufferPercent",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "BufferedCostPerBaseUnit",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "CostBufferPercent",
                table: "RawMaterials");
        }
    }
}
