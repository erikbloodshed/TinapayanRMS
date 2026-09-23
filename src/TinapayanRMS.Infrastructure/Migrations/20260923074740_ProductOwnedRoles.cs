using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductOwnedRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsProductOwned",
                table: "SubAssemblyRoles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OwnerProductId",
                table: "SubAssemblies",
                type: "int",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SubAssemblyRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsProductOwned",
                value: false);

            migrationBuilder.UpdateData(
                table: "SubAssemblyRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "IsProductOwned",
                value: false);

            migrationBuilder.UpdateData(
                table: "SubAssemblyRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "IsProductOwned",
                value: false);

            migrationBuilder.UpdateData(
                table: "SubAssemblyRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "IsProductOwned",
                value: false);

            migrationBuilder.UpdateData(
                table: "SubAssemblyRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "IsProductOwned",
                value: false);

            migrationBuilder.UpdateData(
                table: "SubAssemblyRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "IsProductOwned",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblies_OwnerProductId",
                table: "SubAssemblies",
                column: "OwnerProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_SubAssemblies_Products_OwnerProductId",
                table: "SubAssemblies",
                column: "OwnerProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubAssemblies_Products_OwnerProductId",
                table: "SubAssemblies");

            migrationBuilder.DropIndex(
                name: "IX_SubAssemblies_OwnerProductId",
                table: "SubAssemblies");

            migrationBuilder.DropColumn(
                name: "IsProductOwned",
                table: "SubAssemblyRoles");

            migrationBuilder.DropColumn(
                name: "OwnerProductId",
                table: "SubAssemblies");
        }
    }
}
