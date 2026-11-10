using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261110083212_BulkPackaging : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_packaging_lines_product_id_packaging_material_id",
            table: "packaging_lines");

        migrationBuilder.AddColumn<int>(
            name: "bulk_packaging_quantity",
            table: "products",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "is_bulk",
            table: "packaging_lines",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Products_BulkPackagingQuantity",
            table: "products",
            sql: "bulk_packaging_quantity IS NULL OR bulk_packaging_quantity >= 1");

        migrationBuilder.CreateIndex(
            name: "ix_packaging_lines_product_id_packaging_material_id_is_bulk",
            table: "packaging_lines",
            columns: new[] { "product_id", "packaging_material_id", "is_bulk" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Products_BulkPackagingQuantity",
            table: "products");

        migrationBuilder.DropIndex(
            name: "ix_packaging_lines_product_id_packaging_material_id_is_bulk",
            table: "packaging_lines");

        migrationBuilder.DropColumn(
            name: "bulk_packaging_quantity",
            table: "products");

        migrationBuilder.DropColumn(
            name: "is_bulk",
            table: "packaging_lines");

        migrationBuilder.CreateIndex(
            name: "ix_packaging_lines_product_id_packaging_material_id",
            table: "packaging_lines",
            columns: new[] { "product_id", "packaging_material_id" },
            unique: true);
    }
}
