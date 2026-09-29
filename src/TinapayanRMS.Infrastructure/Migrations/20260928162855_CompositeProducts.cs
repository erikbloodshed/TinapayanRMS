using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompositeProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsComposite",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ProductComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ComponentProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductComponents", x => x.Id);
                    table.CheckConstraint("CK_ProductComponents_NotItself", "[ProductId] <> [ComponentProductId]");
                    table.CheckConstraint("CK_ProductComponents_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_ProductComponents_Products_ComponentProductId",
                        column: x => x.ComponentProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductComponents_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_CompositeIsOneUnit",
                table: "Products",
                sql: "[IsComposite] = 0 OR [QuantityRequired] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProductComponents_ComponentProductId",
                table: "ProductComponents",
                column: "ComponentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductComponents_ProductId_ComponentProductId",
                table: "ProductComponents",
                columns: new[] { "ProductId", "ComponentProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductComponents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_CompositeIsOneUnit",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "IsComposite",
                table: "Products");
        }
    }
}
