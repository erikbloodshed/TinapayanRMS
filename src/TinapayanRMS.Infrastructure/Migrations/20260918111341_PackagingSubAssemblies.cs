using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PackagingSubAssemblies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPackaging",
                table: "SubAssemblyRoles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "LinkedProductId",
                table: "PackagingLinks",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "LinkedSubAssemblyId",
                table: "PackagingLinks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubAssemblyPackagingLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubAssemblyId = table.Column<int>(type: "int", nullable: false),
                    PackagingMaterialId = table.Column<int>(type: "int", nullable: false),
                    QtyPerSet = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubAssemblyPackagingLines", x => x.Id);
                    table.CheckConstraint("CK_SubAssemblyPackagingLines_QtyPerSet", "[QtyPerSet] > 0");
                    table.ForeignKey(
                        name: "FK_SubAssemblyPackagingLines_RawMaterials_PackagingMaterialId",
                        column: x => x.PackagingMaterialId,
                        principalTable: "RawMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubAssemblyPackagingLines_SubAssemblies_SubAssemblyId",
                        column: x => x.SubAssemblyId,
                        principalTable: "SubAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "SubAssemblyRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsPackaging",
                value: false);

            migrationBuilder.UpdateData(
                table: "SubAssemblyRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "IsPackaging",
                value: false);

            migrationBuilder.UpdateData(
                table: "SubAssemblyRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "IsPackaging",
                value: false);

            migrationBuilder.UpdateData(
                table: "SubAssemblyRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "IsPackaging",
                value: false);

            migrationBuilder.UpdateData(
                table: "SubAssemblyRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "IsPackaging",
                value: false);

            migrationBuilder.InsertData(
                table: "SubAssemblyRoles",
                columns: new[] { "Id", "IsPackaging", "Name", "SortOrder" },
                values: new object[] { 6, true, "Packaging", 6 });

            migrationBuilder.CreateIndex(
                name: "IX_PackagingLinks_LinkedSubAssemblyId",
                table: "PackagingLinks",
                column: "LinkedSubAssemblyId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PackagingLinks_OneTarget",
                table: "PackagingLinks",
                sql: "([LinkedProductId] IS NOT NULL AND [LinkedSubAssemblyId] IS NULL) OR ([LinkedProductId] IS NULL AND [LinkedSubAssemblyId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblyPackagingLines_PackagingMaterialId",
                table: "SubAssemblyPackagingLines",
                column: "PackagingMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblyPackagingLines_SubAssemblyId_PackagingMaterialId",
                table: "SubAssemblyPackagingLines",
                columns: new[] { "SubAssemblyId", "PackagingMaterialId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PackagingLinks_SubAssemblies_LinkedSubAssemblyId",
                table: "PackagingLinks",
                column: "LinkedSubAssemblyId",
                principalTable: "SubAssemblies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PackagingLinks_SubAssemblies_LinkedSubAssemblyId",
                table: "PackagingLinks");

            migrationBuilder.DropTable(
                name: "SubAssemblyPackagingLines");

            migrationBuilder.DropIndex(
                name: "IX_PackagingLinks_LinkedSubAssemblyId",
                table: "PackagingLinks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PackagingLinks_OneTarget",
                table: "PackagingLinks");

            migrationBuilder.DeleteData(
                table: "SubAssemblyRoles",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DropColumn(
                name: "IsPackaging",
                table: "SubAssemblyRoles");

            migrationBuilder.DropColumn(
                name: "LinkedSubAssemblyId",
                table: "PackagingLinks");

            migrationBuilder.AlterColumn<int>(
                name: "LinkedProductId",
                table: "PackagingLinks",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
