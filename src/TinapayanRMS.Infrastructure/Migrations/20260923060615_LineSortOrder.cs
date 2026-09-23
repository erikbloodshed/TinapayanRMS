using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LineSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "UsageAddInLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "SubAssemblyPackagingLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "SubAssemblyLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "PackagingLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "IngredientBomLines",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "UsageAddInLines");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "SubAssemblyPackagingLines");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "SubAssemblyLines");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "PackagingLines");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "IngredientBomLines");
        }
    }
}
