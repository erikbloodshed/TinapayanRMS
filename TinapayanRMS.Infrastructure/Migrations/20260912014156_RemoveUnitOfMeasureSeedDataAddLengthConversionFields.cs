using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnitOfMeasureSeedDataAddLengthConversionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "UnitsOfMeasure",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "UnitsOfMeasure",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "UnitsOfMeasure",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "UnitsOfMeasure",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "UnitsOfMeasure",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "UnitsOfMeasure",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.AddColumn<bool>(
                name: "IsLengthUnit",
                table: "UnitsOfMeasure",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MillimetersPerUnit",
                table: "UnitsOfMeasure",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsLengthUnit",
                table: "UnitsOfMeasure");

            migrationBuilder.DropColumn(
                name: "MillimetersPerUnit",
                table: "UnitsOfMeasure");

            migrationBuilder.InsertData(
                table: "UnitsOfMeasure",
                columns: new[] { "Id", "Abbreviation", "Name" },
                values: new object[,]
                {
                    { 1, "mm", "Millimeter" },
                    { 2, "cm", "Centimeter" },
                    { 3, "in", "Inch" },
                    { 4, "m", "Meter" },
                    { 5, "g", "Gram" },
                    { 6, "pc", "Piece" }
                });
        }
    }
}
