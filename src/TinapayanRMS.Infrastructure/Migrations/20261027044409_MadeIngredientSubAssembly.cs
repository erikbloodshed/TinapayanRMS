using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261027044409_MadeIngredientSubAssembly : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "sub_assembly_id",
            table: "raw_materials",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "ingredient_add_in_lines",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ingredient_id = table.Column<int>(type: "integer", nullable: false),
                add_in_ingredient_id = table.Column<int>(type: "integer", nullable: false),
                grams = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                sort_order = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ingredient_add_in_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_ingredient_add_in_lines_raw_materials_add_in_ingredient_id",
                    column: x => x.add_in_ingredient_id,
                    principalTable: "raw_materials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_ingredient_add_in_lines_raw_materials_ingredient_id",
                    column: x => x.ingredient_id,
                    principalTable: "raw_materials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_raw_materials_sub_assembly_id",
            table: "raw_materials",
            column: "sub_assembly_id");

        migrationBuilder.CreateIndex(
            name: "ix_ingredient_add_in_lines_add_in_ingredient_id",
            table: "ingredient_add_in_lines",
            column: "add_in_ingredient_id");

        migrationBuilder.CreateIndex(
            name: "ix_ingredient_add_in_lines_ingredient_id_add_in_ingredient_id",
            table: "ingredient_add_in_lines",
            columns: new[] { "ingredient_id", "add_in_ingredient_id" },
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "fk_raw_materials_sub_assemblies_sub_assembly_id",
            table: "raw_materials",
            column: "sub_assembly_id",
            principalTable: "sub_assemblies",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_raw_materials_sub_assemblies_sub_assembly_id",
            table: "raw_materials");

        migrationBuilder.DropTable(
            name: "ingredient_add_in_lines");

        migrationBuilder.DropIndex(
            name: "ix_raw_materials_sub_assembly_id",
            table: "raw_materials");

        migrationBuilder.DropColumn(
            name: "sub_assembly_id",
            table: "raw_materials");
    }
}
