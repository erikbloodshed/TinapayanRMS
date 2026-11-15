using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261115054714_AccessSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "access_settings",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                password_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                password_salt = table.Column<byte[]>(type: "bytea", nullable: true),
                password_iterations = table.Column<int>(type: "integer", nullable: false),
                password_changed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                auto_lock_minutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 15)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_access_settings", x => x.id);
                table.CheckConstraint("CK_AccessSettings_AutoLockMinutes", "auto_lock_minutes >= 0 AND auto_lock_minutes <= 240");
                table.CheckConstraint("CK_AccessSettings_PasswordWhole", "(password_hash IS NULL AND password_salt IS NULL) OR (password_hash IS NOT NULL AND password_salt IS NOT NULL AND password_iterations > 0)");
                table.CheckConstraint("CK_AccessSettings_SingleRow", "id = 1");
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "access_settings");
    }
}
