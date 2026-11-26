using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126144828_RoleCanGoIntoOtherSections : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "can_go_into_other_sections",
            table: "sub_assembly_roles",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.UpdateData(
            table: "sub_assembly_roles",
            keyColumn: "id",
            keyValue: 1,
            column: "can_go_into_other_sections",
            value: false);

        migrationBuilder.UpdateData(
            table: "sub_assembly_roles",
            keyColumn: "id",
            keyValue: 2,
            column: "can_go_into_other_sections",
            value: false);

        migrationBuilder.UpdateData(
            table: "sub_assembly_roles",
            keyColumn: "id",
            keyValue: 3,
            column: "can_go_into_other_sections",
            value: false);

        migrationBuilder.UpdateData(
            table: "sub_assembly_roles",
            keyColumn: "id",
            keyValue: 4,
            column: "can_go_into_other_sections",
            value: false);

        migrationBuilder.UpdateData(
            table: "sub_assembly_roles",
            keyColumn: "id",
            keyValue: 5,
            column: "can_go_into_other_sections",
            value: false);

        migrationBuilder.UpdateData(
            table: "sub_assembly_roles",
            keyColumn: "id",
            keyValue: 6,
            column: "can_go_into_other_sections",
            value: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "can_go_into_other_sections",
            table: "sub_assembly_roles");
    }
}
