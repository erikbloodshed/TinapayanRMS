using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecipeLinesInAlternativeUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IngredientUnitId",
                table: "SubAssemblyLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "SubAssemblyLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "IngredientUnitId",
                table: "IngredientBomLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "IngredientBomLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            // Every existing line was entered in grams (no unit), so that's its quantity. Down needs
            // nothing: Grams was kept correct throughout.
            migrationBuilder.Sql("""
                UPDATE SubAssemblyLines SET Quantity = Grams;
                UPDATE IngredientBomLines SET Quantity = Grams;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblyLines_IngredientUnitId",
                table: "SubAssemblyLines",
                column: "IngredientUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientBomLines_IngredientUnitId",
                table: "IngredientBomLines",
                column: "IngredientUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_IngredientBomLines_IngredientUnits_IngredientUnitId",
                table: "IngredientBomLines",
                column: "IngredientUnitId",
                principalTable: "IngredientUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubAssemblyLines_IngredientUnits_IngredientUnitId",
                table: "SubAssemblyLines",
                column: "IngredientUnitId",
                principalTable: "IngredientUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IngredientBomLines_IngredientUnits_IngredientUnitId",
                table: "IngredientBomLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SubAssemblyLines_IngredientUnits_IngredientUnitId",
                table: "SubAssemblyLines");

            migrationBuilder.DropIndex(
                name: "IX_SubAssemblyLines_IngredientUnitId",
                table: "SubAssemblyLines");

            migrationBuilder.DropIndex(
                name: "IX_IngredientBomLines_IngredientUnitId",
                table: "IngredientBomLines");

            migrationBuilder.DropColumn(
                name: "IngredientUnitId",
                table: "SubAssemblyLines");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "SubAssemblyLines");

            migrationBuilder.DropColumn(
                name: "IngredientUnitId",
                table: "IngredientBomLines");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "IngredientBomLines");
        }
    }
}
