using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261112122740_ProductFolders : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "folder_id",
            table: "products",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "product_folders",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "case_insensitive"),
                stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                parent_id = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_product_folders", x => x.id);
                table.CheckConstraint("CK_ProductFolders_NotOwnParent", "parent_id IS NULL OR parent_id <> id");
                table.ForeignKey(
                    name: "fk_product_folders_product_folders_parent_id",
                    column: x => x.parent_id,
                    principalTable: "product_folders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_products_folder_id",
            table: "products",
            column: "folder_id");

        migrationBuilder.CreateIndex(
            name: "ix_product_folders_parent_id",
            table: "product_folders",
            column: "parent_id");

        migrationBuilder.CreateIndex(
            name: "ix_product_folders_stage_parent_id_name",
            table: "product_folders",
            columns: new[] { "stage", "parent_id", "name" },
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);

        migrationBuilder.AddForeignKey(
            name: "fk_products_product_folders_folder_id",
            table: "products",
            column: "folder_id",
            principalTable: "product_folders",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_products_product_folders_folder_id",
            table: "products");

        migrationBuilder.DropTable(
            name: "product_folders");

        migrationBuilder.DropIndex(
            name: "ix_products_folder_id",
            table: "products");

        migrationBuilder.DropColumn(
            name: "folder_id",
            table: "products");
    }
}
