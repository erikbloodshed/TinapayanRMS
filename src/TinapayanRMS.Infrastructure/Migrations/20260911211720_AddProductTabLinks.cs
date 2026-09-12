using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductTabLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductTabLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    TabKind = table.Column<int>(type: "int", nullable: false),
                    SubAssemblyRoleId = table.Column<int>(type: "int", nullable: true),
                    LinkedProductId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTabLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductTabLinks_Products_LinkedProductId",
                        column: x => x.LinkedProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductTabLinks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductTabLinks_SubAssemblyRoles_SubAssemblyRoleId",
                        column: x => x.SubAssemblyRoleId,
                        principalTable: "SubAssemblyRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductTabLinks_LinkedProductId",
                table: "ProductTabLinks",
                column: "LinkedProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductTabLinks_ProductId",
                table: "ProductTabLinks",
                column: "ProductId",
                unique: true,
                filter: "[SubAssemblyRoleId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductTabLinks_ProductId_SubAssemblyRoleId",
                table: "ProductTabLinks",
                columns: new[] { "ProductId", "SubAssemblyRoleId" },
                unique: true,
                filter: "[SubAssemblyRoleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductTabLinks_SubAssemblyRoleId",
                table: "ProductTabLinks",
                column: "SubAssemblyRoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductTabLinks");
        }
    }
}
