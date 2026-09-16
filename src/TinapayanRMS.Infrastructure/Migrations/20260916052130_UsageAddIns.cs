using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UsageAddIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsageAddInLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsageId = table.Column<int>(type: "int", nullable: false),
                    IngredientId = table.Column<int>(type: "int", nullable: false),
                    Grams = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageAddInLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsageAddInLines_ProductSubAssemblyUsages_UsageId",
                        column: x => x.UsageId,
                        principalTable: "ProductSubAssemblyUsages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UsageAddInLines_RawMaterials_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "RawMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsageAddInLines_IngredientId",
                table: "UsageAddInLines",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageAddInLines_UsageId_IngredientId",
                table: "UsageAddInLines",
                columns: new[] { "UsageId", "IngredientId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsageAddInLines");
        }
    }
}
