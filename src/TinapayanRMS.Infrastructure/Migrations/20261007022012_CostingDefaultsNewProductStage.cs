using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CostingDefaultsNewProductStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NewProductStage",
                table: "CostingDefaults",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Production");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewProductStage",
                table: "CostingDefaults");
        }
    }
}
