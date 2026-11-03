using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CostingDefaultsPriceRoundingStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "price_rounding_step",
                table: "costing_defaults",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CostingDefaults_PriceRoundingStep",
                table: "costing_defaults",
                sql: "price_rounding_step IN (0, 0.25, 0.5, 1, 5, 10)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CostingDefaults_PriceRoundingStep",
                table: "costing_defaults");

            migrationBuilder.DropColumn(
                name: "price_rounding_step",
                table: "costing_defaults");
        }
    }
}
