using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OptionalUsagePieceSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductSubAssemblyUsages_AuthoredValue",
                table: "ProductSubAssemblyUsages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductSubAssemblyUsages_AuthoredValue",
                table: "ProductSubAssemblyUsages",
                sql: "[AuthoredValue] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductSubAssemblyUsages_AuthoredValue",
                table: "ProductSubAssemblyUsages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductSubAssemblyUsages_AuthoredValue",
                table: "ProductSubAssemblyUsages",
                sql: "[AuthoredValue] > 0");
        }
    }
}
