using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SetPackagingLineUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UnitId",
                table: "SubAssemblyPackagingLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblyPackagingLines_UnitId",
                table: "SubAssemblyPackagingLines",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_SubAssemblyPackagingLines_RawMaterialUnits_UnitId",
                table: "SubAssemblyPackagingLines",
                column: "UnitId",
                principalTable: "RawMaterialUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubAssemblyPackagingLines_RawMaterialUnits_UnitId",
                table: "SubAssemblyPackagingLines");

            migrationBuilder.DropIndex(
                name: "IX_SubAssemblyPackagingLines_UnitId",
                table: "SubAssemblyPackagingLines");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "SubAssemblyPackagingLines");
        }
    }
}
