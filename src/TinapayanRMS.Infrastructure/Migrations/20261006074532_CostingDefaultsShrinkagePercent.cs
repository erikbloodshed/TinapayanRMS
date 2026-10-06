using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CostingDefaultsShrinkagePercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ShrinkagePercent",
                table: "CostingDefaults",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 3m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CostingDefaults_ShrinkagePercent",
                table: "CostingDefaults",
                sql: "[ShrinkagePercent] >= 0 AND [ShrinkagePercent] < 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CostingDefaults_ShrinkagePercent",
                table: "CostingDefaults");

            migrationBuilder.DropColumn(
                name: "ShrinkagePercent",
                table: "CostingDefaults");
        }
    }
}
