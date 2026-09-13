using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IngredientAlternativeUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reordered by hand: the new table and column are created first, so the data SQL below
            // can carry each ingredient's purchase unit over before the old columns are dropped.
            migrationBuilder.AddColumn<int>(
                name: "IngredientUnitId",
                table: "IngredientPriceHistories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IngredientUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IngredientId = table.Column<int>(type: "int", nullable: false),
                    UnitOfMeasureId = table.Column<int>(type: "int", nullable: false),
                    GramsPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientUnits_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IngredientUnits_UnitsOfMeasure_UnitOfMeasureId",
                        column: x => x.UnitOfMeasureId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IngredientPriceHistories_IngredientUnitId",
                table: "IngredientPriceHistories",
                column: "IngredientUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientUnits_IngredientId_UnitOfMeasureId",
                table: "IngredientUnits",
                columns: new[] { "IngredientId", "UnitOfMeasureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngredientUnits_UnitOfMeasureId",
                table: "IngredientUnits",
                column: "UnitOfMeasureId");

            migrationBuilder.AddForeignKey(
                name: "FK_IngredientPriceHistories_IngredientUnits_IngredientUnitId",
                table: "IngredientPriceHistories",
                column: "IngredientUnitId",
                principalTable: "IngredientUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Each purchased ingredient's purchase unit becomes its first alternative unit, and
            // every price it had (all per purchase unit) is now recorded in that unit.
            //
            // CostPerGram is then recomputed from each ingredient's true latest price. Before
            // PriceHistoryRules.FindLatestAfterAdding, a second price entered on the same day never
            // became current, so a stored CostPerGram could be behind its own price history. Only
            // the purchased ingredients themselves are healed here; anything built from them is
            // recomputed the next time one of their costs changes.
            migrationBuilder.Sql("""
                INSERT INTO IngredientUnits (IngredientId, UnitOfMeasureId, GramsPerUnit, Description)
                SELECT Id, PurchaseUnitId, PurchaseUnitQty, NULL
                FROM Ingredients
                WHERE PurchaseUnitId IS NOT NULL AND PurchaseUnitQty > 0;

                UPDATE h
                SET IngredientUnitId = u.Id
                FROM IngredientPriceHistories h
                JOIN Ingredients i ON i.Id = h.IngredientId
                JOIN IngredientUnits u ON u.IngredientId = i.Id AND u.UnitOfMeasureId = i.PurchaseUnitId;

                UPDATE i
                SET CostPerGram = latest.Price / u.GramsPerUnit
                FROM Ingredients i
                CROSS APPLY (SELECT TOP 1 h.Price, h.IngredientUnitId
                             FROM IngredientPriceHistories h
                             WHERE h.IngredientId = i.Id
                             ORDER BY h.Date DESC, h.Id DESC) latest
                JOIN IngredientUnits u ON u.Id = latest.IngredientUnitId;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Ingredients_UnitsOfMeasure_PurchaseUnitId",
                table: "Ingredients");

            migrationBuilder.DropIndex(
                name: "IX_Ingredients_PurchaseUnitId",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "CurrentCost",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "PurchaseUnitId",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "PurchaseUnitQty",
                table: "Ingredients");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reordered by hand, mirroring Up: the old columns come back and are filled before the
            // units table is dropped.
            migrationBuilder.AddColumn<decimal>(
                name: "CurrentCost",
                table: "Ingredients",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseUnitId",
                table: "Ingredients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PurchaseUnitQty",
                table: "Ingredients",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            // The old schema has one purchase unit per ingredient: the unit of its latest price
            // (else its largest unit), and CurrentCost is that latest price. Older prices in other
            // units read as per that purchase unit again, which the old schema can't avoid.
            migrationBuilder.Sql("""
                UPDATE i
                SET PurchaseUnitId = u.UnitOfMeasureId,
                    PurchaseUnitQty = u.GramsPerUnit,
                    CurrentCost = latest.Price
                FROM Ingredients i
                OUTER APPLY (SELECT TOP 1 h.Price, h.IngredientUnitId
                             FROM IngredientPriceHistories h
                             WHERE h.IngredientId = i.Id
                             ORDER BY h.Date DESC, h.Id DESC) latest
                OUTER APPLY (SELECT TOP 1 x.UnitOfMeasureId, x.GramsPerUnit
                             FROM IngredientUnits x
                             WHERE x.IngredientId = i.Id
                             ORDER BY CASE WHEN x.Id = latest.IngredientUnitId THEN 0 ELSE 1 END, x.GramsPerUnit DESC) u
                WHERE i.Source = 'Purchased';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_PurchaseUnitId",
                table: "Ingredients",
                column: "PurchaseUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_Ingredients_UnitsOfMeasure_PurchaseUnitId",
                table: "Ingredients",
                column: "PurchaseUnitId",
                principalTable: "UnitsOfMeasure",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey(
                name: "FK_IngredientPriceHistories_IngredientUnits_IngredientUnitId",
                table: "IngredientPriceHistories");

            migrationBuilder.DropTable(
                name: "IngredientUnits");

            migrationBuilder.DropIndex(
                name: "IX_IngredientPriceHistories_IngredientUnitId",
                table: "IngredientPriceHistories");

            migrationBuilder.DropColumn(
                name: "IngredientUnitId",
                table: "IngredientPriceHistories");
        }
    }
}
