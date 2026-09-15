using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CostingPercentagesOfMaterialCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OverheadCost",
                table: "CostingParameters",
                newName: "OverheadPercent");

            migrationBuilder.RenameColumn(
                name: "LaborCost",
                table: "CostingParameters",
                newName: "LaborPercent");

            migrationBuilder.RenameColumn(
                name: "AdministrativeCost",
                table: "CostingParameters",
                newName: "AdministrativePercent");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OverheadPercent",
                table: "CostingParameters",
                newName: "OverheadCost");

            migrationBuilder.RenameColumn(
                name: "LaborPercent",
                table: "CostingParameters",
                newName: "LaborCost");

            migrationBuilder.RenameColumn(
                name: "AdministrativePercent",
                table: "CostingParameters",
                newName: "AdministrativeCost");
        }
    }
}
