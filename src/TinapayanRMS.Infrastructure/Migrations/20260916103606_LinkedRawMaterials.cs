using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkedRawMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostSharePercent",
                table: "RawMaterials",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LinkedToId",
                table: "RawMaterials",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightSharePercent",
                table: "RawMaterials",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterials_LinkedToId",
                table: "RawMaterials",
                column: "LinkedToId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RawMaterials_LinkMatchesSource",
                table: "RawMaterials",
                sql: "([Source] = 'Linked' AND [LinkedToId] IS NOT NULL AND [WeightSharePercent] > 0 AND [WeightSharePercent] <= 100 AND [CostSharePercent] > 0 AND [CostSharePercent] <= 100) OR ([Source] <> 'Linked' AND [LinkedToId] IS NULL AND [WeightSharePercent] IS NULL AND [CostSharePercent] IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_RawMaterials_RawMaterials_LinkedToId",
                table: "RawMaterials",
                column: "LinkedToId",
                principalTable: "RawMaterials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RawMaterials_RawMaterials_LinkedToId",
                table: "RawMaterials");

            migrationBuilder.DropIndex(
                name: "IX_RawMaterials_LinkedToId",
                table: "RawMaterials");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RawMaterials_LinkMatchesSource",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "CostSharePercent",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "LinkedToId",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "WeightSharePercent",
                table: "RawMaterials");
        }
    }
}
