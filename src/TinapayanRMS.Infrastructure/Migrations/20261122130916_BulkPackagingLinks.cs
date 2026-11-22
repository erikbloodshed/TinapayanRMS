using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261122130916_BulkPackagingLinks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_packaging_links_product_id",
            table: "packaging_links");

        migrationBuilder.AddColumn<bool>(
            name: "is_bulk",
            table: "packaging_links",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "ix_packaging_links_product_id_is_bulk",
            table: "packaging_links",
            columns: new[] { "product_id", "is_bulk" },
            unique: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_PackagingLinks_BulkLinksASet",
            table: "packaging_links",
            sql: "NOT is_bulk OR linked_sub_assembly_id IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_packaging_links_product_id_is_bulk",
            table: "packaging_links");

        migrationBuilder.DropCheckConstraint(
            name: "CK_PackagingLinks_BulkLinksASet",
            table: "packaging_links");

        migrationBuilder.DropColumn(
            name: "is_bulk",
            table: "packaging_links");

        migrationBuilder.CreateIndex(
            name: "ix_packaging_links_product_id",
            table: "packaging_links",
            column: "product_id",
            unique: true);
    }
}
