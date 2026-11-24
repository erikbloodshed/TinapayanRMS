using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261124054152_SectionLines : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_product_sub_assembly_usages_sub_assemblies_goes_into_sub_as",
            table: "product_sub_assembly_usages");

        migrationBuilder.DropIndex(
            name: "ix_product_sub_assembly_usages_goes_into_sub_assembly_id",
            table: "product_sub_assembly_usages");

        migrationBuilder.DropCheckConstraint(
            name: "CK_ProductSubAssemblyUsages_GoesIntoAnother",
            table: "product_sub_assembly_usages");

        migrationBuilder.DropColumn(
            name: "goes_into_sub_assembly_id",
            table: "product_sub_assembly_usages");

        migrationBuilder.CreateTable(
            name: "usage_section_lines",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                usage_id = table.Column<int>(type: "integer", nullable: false),
                section_sub_assembly_id = table.Column<int>(type: "integer", nullable: false),
                grams = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                authored_bakers_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                position = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_usage_section_lines", x => x.id);
                table.CheckConstraint("CK_UsageSectionLines_Grams", "grams > 0");
                table.ForeignKey(
                    name: "fk_usage_section_lines_product_sub_assembly_usages_usage_id",
                    column: x => x.usage_id,
                    principalTable: "product_sub_assembly_usages",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_usage_section_lines_sub_assemblies_section_sub_assembly_id",
                    column: x => x.section_sub_assembly_id,
                    principalTable: "sub_assemblies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_usage_section_lines_section_sub_assembly_id",
            table: "usage_section_lines",
            column: "section_sub_assembly_id");

        migrationBuilder.CreateIndex(
            name: "ix_usage_section_lines_usage_id_section_sub_assembly_id",
            table: "usage_section_lines",
            columns: new[] { "usage_id", "section_sub_assembly_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "usage_section_lines");

        migrationBuilder.AddColumn<int>(
            name: "goes_into_sub_assembly_id",
            table: "product_sub_assembly_usages",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_product_sub_assembly_usages_goes_into_sub_assembly_id",
            table: "product_sub_assembly_usages",
            column: "goes_into_sub_assembly_id");

        migrationBuilder.AddCheckConstraint(
            name: "CK_ProductSubAssemblyUsages_GoesIntoAnother",
            table: "product_sub_assembly_usages",
            sql: "goes_into_sub_assembly_id IS NULL OR goes_into_sub_assembly_id <> sub_assembly_id");

        migrationBuilder.AddForeignKey(
            name: "fk_product_sub_assembly_usages_sub_assemblies_goes_into_sub_as",
            table: "product_sub_assembly_usages",
            column: "goes_into_sub_assembly_id",
            principalTable: "sub_assemblies",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }
}
