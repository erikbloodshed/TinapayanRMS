using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SavedPlanDayAndResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "make_on",
                table: "saved_plans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "leftover_units",
                table: "saved_plan_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "made_units",
                table: "saved_plan_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SavedPlanLines_LeftoverUnits",
                table: "saved_plan_lines",
                sql: "leftover_units IS NULL OR (made_units IS NOT NULL AND leftover_units >= 0 AND leftover_units <= made_units)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SavedPlanLines_MadeUnits",
                table: "saved_plan_lines",
                sql: "made_units IS NULL OR made_units >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SavedPlanLines_LeftoverUnits",
                table: "saved_plan_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SavedPlanLines_MadeUnits",
                table: "saved_plan_lines");

            migrationBuilder.DropColumn(
                name: "make_on",
                table: "saved_plans");

            migrationBuilder.DropColumn(
                name: "leftover_units",
                table: "saved_plan_lines");

            migrationBuilder.DropColumn(
                name: "made_units",
                table: "saved_plan_lines");
        }
    }
}
