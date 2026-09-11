using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubAssemblies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubAssemblyRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubAssemblyRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubAssemblies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    TotalWeight = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubAssemblies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubAssemblies_SubAssemblyRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "SubAssemblyRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubAssemblyLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubAssemblyId = table.Column<int>(type: "int", nullable: false),
                    IngredientId = table.Column<int>(type: "int", nullable: false),
                    Grams = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubAssemblyLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubAssemblyLines_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubAssemblyLines_SubAssemblies_SubAssemblyId",
                        column: x => x.SubAssemblyId,
                        principalTable: "SubAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "SubAssemblyRoles",
                columns: new[] { "Id", "Name", "SortOrder" },
                values: new object[,]
                {
                    { 1, "Dough", 1 },
                    { 2, "Filling", 2 },
                    { 3, "Topping", 3 },
                    { 4, "Inclusion", 4 },
                    { 5, "Finishing", 5 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblies_Name",
                table: "SubAssemblies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblies_RoleId",
                table: "SubAssemblies",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblyLines_IngredientId",
                table: "SubAssemblyLines",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblyLines_SubAssemblyId_IngredientId",
                table: "SubAssemblyLines",
                columns: new[] { "SubAssemblyId", "IngredientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblyRoles_Name",
                table: "SubAssemblyRoles",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubAssemblyLines");

            migrationBuilder.DropTable(
                name: "SubAssemblies");

            migrationBuilder.DropTable(
                name: "SubAssemblyRoles");
        }
    }
}
