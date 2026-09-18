using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PackagingLinesServeUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "QtyPerPiece",
                table: "PackagingLines",
                newName: "QtyPerUnit");

            migrationBuilder.RenameColumn(
                name: "PiecesPerPackagingUnit",
                table: "PackagingLines",
                newName: "UnitsPerPackagingUnit");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UnitsPerPackagingUnit",
                table: "PackagingLines",
                newName: "PiecesPerPackagingUnit");

            migrationBuilder.RenameColumn(
                name: "QtyPerUnit",
                table: "PackagingLines",
                newName: "QtyPerPiece");
        }
    }
}
