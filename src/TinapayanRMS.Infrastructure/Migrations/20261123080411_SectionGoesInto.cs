using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261123080411_SectionGoesInto : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
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
    }
}
