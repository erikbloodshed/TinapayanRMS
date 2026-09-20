using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductQuantityRequiredReplacesPacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-added, as DropLengthUnitsAndPackagingLineEntryUnit's conversions were: a pack's own lines
            // have to go with the pack. Dropping PackId alone would leave them as ordinary packaging lines of
            // their product — a box charged on every sellable unit — and two packs sharing a material, or a
            // material that wrapped the piece and packed a pack, would break the new
            // IX_PackagingLines_ProductId_PackagingMaterialId as it is created below. A pack is now a product
            // of its own with a Quantity Required, so these are re-entered rather than carried over.
            migrationBuilder.Sql("DELETE FROM PackagingLines WHERE PackId IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_PackagingLines_ProductPacks_PackId",
                table: "PackagingLines");

            migrationBuilder.DropTable(
                name: "ProductPacks");

            migrationBuilder.DropIndex(
                name: "IX_PackagingLines_PackId",
                table: "PackagingLines");

            migrationBuilder.DropIndex(
                name: "IX_PackagingLines_ProductId_PackId_PackagingMaterialId",
                table: "PackagingLines");

            migrationBuilder.DropColumn(
                name: "PackId",
                table: "PackagingLines");

            migrationBuilder.AddColumn<int>(
                name: "QuantityRequired",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_QuantityRequired",
                table: "Products",
                sql: "[QuantityRequired] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingLines_ProductId_PackagingMaterialId",
                table: "PackagingLines",
                columns: new[] { "ProductId", "PackagingMaterialId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_QuantityRequired",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_PackagingLines_ProductId_PackagingMaterialId",
                table: "PackagingLines");

            migrationBuilder.DropColumn(
                name: "QuantityRequired",
                table: "Products");

            migrationBuilder.AddColumn<int>(
                name: "PackId",
                table: "PackagingLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductPacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PiecesPerPack = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPacks", x => x.Id);
                    table.CheckConstraint("CK_ProductPacks_PiecesPerPack", "[PiecesPerPack] > 0");
                    table.ForeignKey(
                        name: "FK_ProductPacks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackagingLines_PackId",
                table: "PackagingLines",
                column: "PackId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingLines_ProductId_PackId_PackagingMaterialId",
                table: "PackagingLines",
                columns: new[] { "ProductId", "PackId", "PackagingMaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductPacks_ProductId_Name",
                table: "ProductPacks",
                columns: new[] { "ProductId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PackagingLines_ProductPacks_PackId",
                table: "PackagingLines",
                column: "PackId",
                principalTable: "ProductPacks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
