using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SellablePacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PackagingLines_ProductId_PackagingMaterialId",
                table: "PackagingLines");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "IX_PackagingLines_ProductId_PackagingMaterialId",
                table: "PackagingLines",
                columns: new[] { "ProductId", "PackagingMaterialId" },
                unique: true);
        }
    }
}
