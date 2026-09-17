using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductSubAssembliesGoByRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubAssemblies_OwnerProductId_Name",
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

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblies_OwnerProductId_Name",
                table: "SubAssemblies",
                columns: new[] { "OwnerProductId", "Name" },
                unique: true,
                filter: "[Name] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubAssemblies_OwnerProductId_Name",
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
                name: "IX_SubAssemblies_OwnerProductId_Name",
                table: "SubAssemblies",
                columns: new[] { "OwnerProductId", "Name" },
                unique: true);
        }
    }
}
