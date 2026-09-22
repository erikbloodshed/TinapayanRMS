using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UsageShrinkagePercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ShrinkagePercent",
                table: "ProductSubAssemblyUsages",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductSubAssemblyUsages_ShrinkagePercent",
                table: "ProductSubAssemblyUsages",
                sql: "[ShrinkagePercent] >= 0 AND [ShrinkagePercent] < 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductSubAssemblyUsages_ShrinkagePercent",
                table: "ProductSubAssemblyUsages");

            migrationBuilder.DropColumn(
                name: "ShrinkagePercent",
                table: "ProductSubAssemblyUsages");
        }
    }
}
