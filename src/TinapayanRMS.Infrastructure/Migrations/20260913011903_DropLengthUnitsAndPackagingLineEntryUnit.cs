using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropLengthUnitsAndPackagingLineEntryUnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Lines are now always in the material's standard unit. Convert any line entered in
            // another length unit (a cm ribbon entered in inches) while the factors still exist.
            migrationBuilder.Sql("""
                UPDATE l
                SET QtyPerPiece = l.QtyPerPiece * eu.MillimetersPerUnit / su.MillimetersPerUnit,
                    PiecesPerPackagingUnit = l.PiecesPerPackagingUnit * su.MillimetersPerUnit / eu.MillimetersPerUnit
                FROM PackagingLines l
                JOIN PackagingMaterials m ON m.Id = l.PackagingMaterialId
                JOIN UnitsOfMeasure eu ON eu.Id = l.EntryUnitId
                JOIN UnitsOfMeasure su ON su.Id = m.StandardUnitId
                WHERE l.EntryUnitId <> m.StandardUnitId
                  AND eu.MillimetersPerUnit IS NOT NULL
                  AND su.MillimetersPerUnit IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_PackagingLines_UnitsOfMeasure_EntryUnitId",
                table: "PackagingLines");

            migrationBuilder.DropIndex(
                name: "IX_PackagingLines_EntryUnitId",
                table: "PackagingLines");

            migrationBuilder.DropColumn(
                name: "IsLengthUnit",
                table: "UnitsOfMeasure");

            migrationBuilder.DropColumn(
                name: "MillimetersPerUnit",
                table: "UnitsOfMeasure");

            migrationBuilder.DropColumn(
                name: "EntryUnitId",
                table: "PackagingLines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLengthUnit",
                table: "UnitsOfMeasure",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MillimetersPerUnit",
                table: "UnitsOfMeasure",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EntryUnitId",
                table: "PackagingLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Every line was in its material's standard unit, so that's its entry unit.
            migrationBuilder.Sql("""
                UPDATE l
                SET EntryUnitId = m.StandardUnitId
                FROM PackagingLines l
                JOIN PackagingMaterials m ON m.Id = l.PackagingMaterialId;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PackagingLines_EntryUnitId",
                table: "PackagingLines",
                column: "EntryUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_PackagingLines_UnitsOfMeasure_EntryUnitId",
                table: "PackagingLines",
                column: "EntryUnitId",
                principalTable: "UnitsOfMeasure",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
