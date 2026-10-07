using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CostingDefaultsCostBufferPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostBufferPercent",
                table: "CostingDefaults",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 10m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CostingDefaults_CostBufferPercent",
                table: "CostingDefaults",
                sql: "[CostBufferPercent] >= 0 AND [CostBufferPercent] <= 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CostingDefaults_CostBufferPercent",
                table: "CostingDefaults");

            migrationBuilder.DropColumn(
                name: "CostBufferPercent",
                table: "CostingDefaults");
        }
    }
}
