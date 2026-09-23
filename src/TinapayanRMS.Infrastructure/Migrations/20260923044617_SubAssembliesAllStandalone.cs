using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SubAssembliesAllStandalone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubAssemblies_Products_OwnerProductId",
                table: "SubAssemblies");

            migrationBuilder.DropIndex(
                name: "IX_SubAssemblies_OwnerProductId_Name",
                table: "SubAssemblies");

            migrationBuilder.DropColumn(
                name: "OwnerProductId",
                table: "SubAssemblies");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "SubAssemblies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblies_Name",
                table: "SubAssemblies",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubAssemblies_Name",
                table: "SubAssemblies");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "SubAssemblies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<int>(
                name: "OwnerProductId",
                table: "SubAssemblies",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblies_OwnerProductId_Name",
                table: "SubAssemblies",
                columns: new[] { "OwnerProductId", "Name" },
                unique: true,
                filter: "[Name] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SubAssemblies_Products_OwnerProductId",
                table: "SubAssemblies",
                column: "OwnerProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
