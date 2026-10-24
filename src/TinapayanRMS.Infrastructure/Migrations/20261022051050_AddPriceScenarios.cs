using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceScenarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "price_scenarios",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "case_insensitive"),
                    saved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_scenarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "price_scenario_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    price_scenario_id = table.Column<int>(type: "integer", nullable: false),
                    raw_material_id = table.Column<int>(type: "integer", nullable: false),
                    change_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    new_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_scenario_lines", x => x.id);
                    table.CheckConstraint("CK_PriceScenarioLines_ChangePercent", "change_percent > -100");
                    table.CheckConstraint("CK_PriceScenarioLines_NewPrice", "new_price > 0");
                    table.CheckConstraint("CK_PriceScenarioLines_OneTyped", "(change_percent IS NULL) <> (new_price IS NULL)");
                    table.ForeignKey(
                        name: "fk_price_scenario_lines_price_scenarios_price_scenario_id",
                        column: x => x.price_scenario_id,
                        principalTable: "price_scenarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_price_scenario_lines_raw_materials_raw_material_id",
                        column: x => x.raw_material_id,
                        principalTable: "raw_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_price_scenario_lines_price_scenario_id_raw_material_id",
                table: "price_scenario_lines",
                columns: new[] { "price_scenario_id", "raw_material_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_scenario_lines_raw_material_id",
                table: "price_scenario_lines",
                column: "raw_material_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_scenarios_name",
                table: "price_scenarios",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "price_scenario_lines");

            migrationBuilder.DropTable(
                name: "price_scenarios");
        }
    }
}
