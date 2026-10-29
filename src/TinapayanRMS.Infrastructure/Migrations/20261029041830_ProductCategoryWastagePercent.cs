using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductCategoryWastagePercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "wastage_percent",
                table: "product_categories",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductCategories_WastagePercent",
                table: "product_categories",
                sql: "wastage_percent IS NULL OR (wastage_percent >= 0 AND wastage_percent <= 100)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductCategories_WastagePercent",
                table: "product_categories");

            migrationBuilder.DropColumn(
                name: "wastage_percent",
                table: "product_categories");
        }
    }
}
