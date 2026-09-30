using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompositeComponentPackaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludesComponentPackaging",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_ComponentPackagingOnComposite",
                table: "Products",
                sql: "[IsComposite] = 1 OR [IncludesComponentPackaging] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_ComponentPackagingOnComposite",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "IncludesComponentPackaging",
                table: "Products");
        }
    }
}
