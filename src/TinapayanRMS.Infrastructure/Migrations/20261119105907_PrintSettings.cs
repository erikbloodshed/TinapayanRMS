using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261119105907_PrintSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "print_settings",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                paper_size = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                orientation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                margins = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_print_settings", x => x.id);
                table.CheckConstraint("CK_PrintSettings_Margins", "margins IN ('Normal', 'Narrow', 'Wide')");
                table.CheckConstraint("CK_PrintSettings_Orientation", "orientation IN ('Portrait', 'Landscape')");
                table.CheckConstraint("CK_PrintSettings_PaperSize", "paper_size IN ('A4', 'Letter', 'LongBond', 'Legal')");
                table.CheckConstraint("CK_PrintSettings_SingleRow", "id = 1");
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "print_settings");
    }
}
