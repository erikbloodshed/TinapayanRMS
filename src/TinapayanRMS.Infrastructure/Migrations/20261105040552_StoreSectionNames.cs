using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261105040552_StoreSectionNames : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "section_name",
            table: "sub_assemblies",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true,
            collation: "case_insensitive");

        migrationBuilder.CreateTable(
            name: "data_upgrades",
            columns: table => new
            {
                id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "case_insensitive"),
                applied_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_data_upgrades", x => x.id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "data_upgrades");

        migrationBuilder.DropColumn(
            name: "section_name",
            table: "sub_assemblies");
    }
}
