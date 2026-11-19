using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261119114520_PrintoutLook : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "footer_note",
            table: "print_settings",
            type: "character varying(150)",
            maxLength: 150,
            nullable: true,
            collation: "case_insensitive");

        migrationBuilder.AddColumn<byte[]>(
            name: "logo",
            table: "print_settings",
            type: "bytea",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "logo_content_type",
            table: "print_settings",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true,
            collation: "case_insensitive");

        migrationBuilder.AddColumn<bool>(
            name: "omit_figures",
            table: "print_settings",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "omit_percentages",
            table: "print_settings",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "omit_price_comparison",
            table: "print_settings",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "omit_unit_costs",
            table: "print_settings",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "shop_address",
            table: "print_settings",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true,
            collation: "case_insensitive");

        migrationBuilder.AddColumn<string>(
            name: "shop_name",
            table: "print_settings",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true,
            collation: "case_insensitive");

        migrationBuilder.AddColumn<string>(
            name: "type_size",
            table: "print_settings",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Normal");

        migrationBuilder.AddCheckConstraint(
            name: "CK_PrintSettings_LogoSize",
            table: "print_settings",
            sql: "logo IS NULL OR octet_length(logo) <= 524288");

        migrationBuilder.AddCheckConstraint(
            name: "CK_PrintSettings_LogoWhole",
            table: "print_settings",
            sql: "(logo IS NULL AND logo_content_type IS NULL) OR (logo IS NOT NULL AND logo_content_type IS NOT NULL AND logo_content_type IN ('image/png', 'image/jpeg'))");

        migrationBuilder.AddCheckConstraint(
            name: "CK_PrintSettings_TypeSize",
            table: "print_settings",
            sql: "type_size IN ('Normal', 'Compact', 'Large')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_PrintSettings_LogoSize",
            table: "print_settings");

        migrationBuilder.DropCheckConstraint(
            name: "CK_PrintSettings_LogoWhole",
            table: "print_settings");

        migrationBuilder.DropCheckConstraint(
            name: "CK_PrintSettings_TypeSize",
            table: "print_settings");

        migrationBuilder.DropColumn(
            name: "footer_note",
            table: "print_settings");

        migrationBuilder.DropColumn(
            name: "logo",
            table: "print_settings");

        migrationBuilder.DropColumn(
            name: "logo_content_type",
            table: "print_settings");

        migrationBuilder.DropColumn(
            name: "omit_figures",
            table: "print_settings");

        migrationBuilder.DropColumn(
            name: "omit_percentages",
            table: "print_settings");

        migrationBuilder.DropColumn(
            name: "omit_price_comparison",
            table: "print_settings");

        migrationBuilder.DropColumn(
            name: "omit_unit_costs",
            table: "print_settings");

        migrationBuilder.DropColumn(
            name: "shop_address",
            table: "print_settings");

        migrationBuilder.DropColumn(
            name: "shop_name",
            table: "print_settings");

        migrationBuilder.DropColumn(
            name: "type_size",
            table: "print_settings");
    }
}
