using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RawMaterialTargetCostBuffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "target_cost_buffer_percent",
                table: "raw_materials",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RawMaterials_TargetCostBufferPercent",
                table: "raw_materials",
                sql: "target_cost_buffer_percent IS NULL OR (target_cost_buffer_percent >= 0 AND target_cost_buffer_percent <= 100)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RawMaterials_TargetCostBufferPercent",
                table: "raw_materials");

            migrationBuilder.DropColumn(
                name: "target_cost_buffer_percent",
                table: "raw_materials");
        }
    }
}
