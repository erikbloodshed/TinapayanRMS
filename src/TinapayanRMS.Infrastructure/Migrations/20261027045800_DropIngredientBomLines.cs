using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261027045800_DropIngredientBomLines : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ingredient_bom_lines");

        migrationBuilder.AddCheckConstraint(
            name: "CK_RawMaterials_SubAssemblyMatchesSource",
            table: "raw_materials",
            sql: "(source = 'Made') = (sub_assembly_id IS NOT NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_RawMaterials_SubAssemblyMatchesSource",
            table: "raw_materials");

        migrationBuilder.CreateTable(
            name: "ingredient_bom_lines",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                component_ingredient_id = table.Column<int>(type: "integer", nullable: false),
                ingredient_id = table.Column<int>(type: "integer", nullable: false),
                grams = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                sort_order = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ingredient_bom_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_ingredient_bom_lines_raw_materials_component_ingredient_id",
                    column: x => x.component_ingredient_id,
                    principalTable: "raw_materials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_ingredient_bom_lines_raw_materials_ingredient_id",
                    column: x => x.ingredient_id,
                    principalTable: "raw_materials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_ingredient_bom_lines_component_ingredient_id",
            table: "ingredient_bom_lines",
            column: "component_ingredient_id");

        migrationBuilder.CreateIndex(
            name: "ix_ingredient_bom_lines_ingredient_id_component_ingredient_id",
            table: "ingredient_bom_lines",
            columns: new[] { "ingredient_id", "component_ingredient_id" },
            unique: true);
    }
}
