using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "saved_plans",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "case_insensitive"),
                    saved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    half_batches = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saved_plan_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    saved_plan_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    units = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_plan_lines", x => x.id);
                    table.CheckConstraint("CK_SavedPlanLines_Units", "units > 0");
                    table.ForeignKey(
                        name: "fk_saved_plan_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_saved_plan_lines_saved_plans_saved_plan_id",
                        column: x => x.saved_plan_id,
                        principalTable: "saved_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_saved_plan_lines_product_id",
                table: "saved_plan_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_plan_lines_saved_plan_id",
                table: "saved_plan_lines",
                column: "saved_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_plans_name",
                table: "saved_plans",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "saved_plan_lines");

            migrationBuilder.DropTable(
                name: "saved_plans");
        }
    }
}
