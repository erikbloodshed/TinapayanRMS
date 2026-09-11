using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MadeIngredientsBom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "WeightLossPercent",
                table: "Ingredients",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IngredientBomLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IngredientId = table.Column<int>(type: "int", nullable: false),
                    ComponentIngredientId = table.Column<int>(type: "int", nullable: false),
                    Grams = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientBomLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientBomLines_Ingredients_ComponentIngredientId",
                        column: x => x.ComponentIngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IngredientBomLines_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IngredientBomLines_ComponentIngredientId",
                table: "IngredientBomLines",
                column: "ComponentIngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientBomLines_IngredientId_ComponentIngredientId",
                table: "IngredientBomLines",
                columns: new[] { "IngredientId", "ComponentIngredientId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IngredientBomLines");

            migrationBuilder.DropColumn(
                name: "WeightLossPercent",
                table: "Ingredients");
        }
    }
}
