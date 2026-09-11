using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CostingParameters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    LaborCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OverheadCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MarginPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostingParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostingParameters_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackagingLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    PackagingMaterialId = table.Column<int>(type: "int", nullable: false),
                    EntryUnitId = table.Column<int>(type: "int", nullable: false),
                    QtyPerPiece = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PiecesPerPackagingUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AuthoredField = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackagingLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackagingLines_PackagingMaterials_PackagingMaterialId",
                        column: x => x.PackagingMaterialId,
                        principalTable: "PackagingMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackagingLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackagingLines_UnitsOfMeasure_EntryUnitId",
                        column: x => x.EntryUnitId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductSubAssemblyUsages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    SubAssemblyId = table.Column<int>(type: "int", nullable: false),
                    WeightPerPiece = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSubAssemblyUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductSubAssemblyUsages_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductSubAssemblyUsages_SubAssemblies_SubAssemblyId",
                        column: x => x.SubAssemblyId,
                        principalTable: "SubAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CostingParameters_ProductId",
                table: "CostingParameters",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackagingLines_EntryUnitId",
                table: "PackagingLines",
                column: "EntryUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingLines_PackagingMaterialId",
                table: "PackagingLines",
                column: "PackagingMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingLines_ProductId_PackagingMaterialId",
                table: "PackagingLines",
                columns: new[] { "ProductId", "PackagingMaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductSubAssemblyUsages_ProductId_SubAssemblyId",
                table: "ProductSubAssemblyUsages",
                columns: new[] { "ProductId", "SubAssemblyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductSubAssemblyUsages_SubAssemblyId",
                table: "ProductSubAssemblyUsages",
                column: "SubAssemblyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CostingParameters");

            migrationBuilder.DropTable(
                name: "PackagingLines");

            migrationBuilder.DropTable(
                name: "ProductSubAssemblyUsages");

            migrationBuilder.DropTable(
                name: "Products");
        }
    }
}
