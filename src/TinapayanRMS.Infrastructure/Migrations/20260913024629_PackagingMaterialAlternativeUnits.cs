using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PackagingMaterialAlternativeUnits : Migration
    {
        // Rewritten by hand. The scaffold took PurchaseUnitQty -> CostPerBaseUnit for a rename (both
        // decimals), which would have turned every material's quantity into its cost; and it dropped
        // the purchase columns before the data they hold could be carried over.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // StandardUnit -> BaseUnit is a real rename.
            migrationBuilder.DropForeignKey(
                name: "FK_PackagingMaterials_UnitsOfMeasure_StandardUnitId",
                table: "PackagingMaterials");

            migrationBuilder.RenameColumn(
                name: "StandardUnitId",
                table: "PackagingMaterials",
                newName: "BaseUnitId");

            migrationBuilder.RenameIndex(
                name: "IX_PackagingMaterials_StandardUnitId",
                table: "PackagingMaterials",
                newName: "IX_PackagingMaterials_BaseUnitId");

            migrationBuilder.AddColumn<decimal>(
                name: "CostPerBaseUnit",
                table: "PackagingMaterials",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PackagingMaterialUnitId",
                table: "PackagingPriceHistories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackagingMaterialUnitId",
                table: "PackagingLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PackagingMaterialUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackagingMaterialId = table.Column<int>(type: "int", nullable: false),
                    UnitOfMeasureId = table.Column<int>(type: "int", nullable: false),
                    BaseUnitsPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackagingMaterialUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackagingMaterialUnits_PackagingMaterials_PackagingMaterialId",
                        column: x => x.PackagingMaterialId,
                        principalTable: "PackagingMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackagingMaterialUnits_UnitsOfMeasure_UnitOfMeasureId",
                        column: x => x.UnitOfMeasureId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackagingPriceHistories_PackagingMaterialUnitId",
                table: "PackagingPriceHistories",
                column: "PackagingMaterialUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingLines_PackagingMaterialUnitId",
                table: "PackagingLines",
                column: "PackagingMaterialUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingMaterialUnits_PackagingMaterialId_UnitOfMeasureId",
                table: "PackagingMaterialUnits",
                columns: new[] { "PackagingMaterialId", "UnitOfMeasureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackagingMaterialUnits_UnitOfMeasureId",
                table: "PackagingMaterialUnits",
                column: "UnitOfMeasureId");

            // A purchase unit other than the base unit becomes the material's first alternative unit,
            // and every price (all per purchase unit) is recorded in it. Where the purchase unit was
            // the base unit itself, prices become per base unit. Existing packaging lines are already
            // in the base unit (DropLengthUnitsAndPackagingLineEntryUnit), so they keep no unit.
            // Then CostPerBaseUnit comes from each material's latest price.
            migrationBuilder.Sql("""
                INSERT INTO PackagingMaterialUnits (PackagingMaterialId, UnitOfMeasureId, BaseUnitsPerUnit, Description)
                SELECT Id, PurchaseUnitId, PurchaseUnitQty, NULL
                FROM PackagingMaterials
                WHERE PurchaseUnitId <> BaseUnitId AND PurchaseUnitQty > 0;

                UPDATE h
                SET PackagingMaterialUnitId = u.Id
                FROM PackagingPriceHistories h
                JOIN PackagingMaterials m ON m.Id = h.PackagingMaterialId
                JOIN PackagingMaterialUnits u ON u.PackagingMaterialId = m.Id AND u.UnitOfMeasureId = m.PurchaseUnitId;

                UPDATE h
                SET Price = h.Price / m.PurchaseUnitQty
                FROM PackagingPriceHistories h
                JOIN PackagingMaterials m ON m.Id = h.PackagingMaterialId
                WHERE m.PurchaseUnitId = m.BaseUnitId AND m.PurchaseUnitQty > 0 AND m.PurchaseUnitQty <> 1;

                UPDATE m
                SET CostPerBaseUnit = latest.Price / COALESCE(u.BaseUnitsPerUnit, 1)
                FROM PackagingMaterials m
                CROSS APPLY (SELECT TOP 1 h.Price, h.PackagingMaterialUnitId
                             FROM PackagingPriceHistories h
                             WHERE h.PackagingMaterialId = m.Id
                             ORDER BY h.Date DESC, h.Id DESC) latest
                LEFT JOIN PackagingMaterialUnits u ON u.Id = latest.PackagingMaterialUnitId;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_PackagingMaterials_UnitsOfMeasure_PurchaseUnitId",
                table: "PackagingMaterials");

            migrationBuilder.DropIndex(
                name: "IX_PackagingMaterials_PurchaseUnitId",
                table: "PackagingMaterials");

            migrationBuilder.DropColumn(
                name: "CurrentCost",
                table: "PackagingMaterials");

            migrationBuilder.DropColumn(
                name: "PurchaseUnitId",
                table: "PackagingMaterials");

            migrationBuilder.DropColumn(
                name: "PurchaseUnitQty",
                table: "PackagingMaterials");

            migrationBuilder.AddForeignKey(
                name: "FK_PackagingLines_PackagingMaterialUnits_PackagingMaterialUnitId",
                table: "PackagingLines",
                column: "PackagingMaterialUnitId",
                principalTable: "PackagingMaterialUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PackagingMaterials_UnitsOfMeasure_BaseUnitId",
                table: "PackagingMaterials",
                column: "BaseUnitId",
                principalTable: "UnitsOfMeasure",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PackagingPriceHistories_PackagingMaterialUnits_PackagingMaterialUnitId",
                table: "PackagingPriceHistories",
                column: "PackagingMaterialUnitId",
                principalTable: "PackagingMaterialUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PackagingLines_PackagingMaterialUnits_PackagingMaterialUnitId",
                table: "PackagingLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PackagingMaterials_UnitsOfMeasure_BaseUnitId",
                table: "PackagingMaterials");

            migrationBuilder.DropForeignKey(
                name: "FK_PackagingPriceHistories_PackagingMaterialUnits_PackagingMaterialUnitId",
                table: "PackagingPriceHistories");

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentCost",
                table: "PackagingMaterials",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseUnitId",
                table: "PackagingMaterials",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PurchaseUnitQty",
                table: "PackagingMaterials",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            // The old schema has one purchase unit per material: the unit of its latest price (else
            // its largest unit, else its base unit). Every price is restated per that purchase unit,
            // and every packaging line in an alternative unit is restated in the base unit, the only
            // unit the old schema's lines had.
            migrationBuilder.Sql("""
                UPDATE m
                SET PurchaseUnitId = COALESCE(pu.UnitOfMeasureId, m.BaseUnitId),
                    PurchaseUnitQty = COALESCE(pu.BaseUnitsPerUnit, 1)
                FROM PackagingMaterials m
                OUTER APPLY (SELECT TOP 1 h.PackagingMaterialUnitId, 1 AS IsPriced
                             FROM PackagingPriceHistories h
                             WHERE h.PackagingMaterialId = m.Id
                             ORDER BY h.Date DESC, h.Id DESC) latest
                OUTER APPLY (SELECT TOP 1 x.UnitOfMeasureId, x.BaseUnitsPerUnit
                             FROM PackagingMaterialUnits x
                             WHERE x.PackagingMaterialId = m.Id
                               AND (latest.IsPriced IS NULL OR x.Id = latest.PackagingMaterialUnitId)
                             ORDER BY x.BaseUnitsPerUnit DESC) pu;

                UPDATE h
                SET Price = h.Price / COALESCE(u.BaseUnitsPerUnit, 1) * m.PurchaseUnitQty
                FROM PackagingPriceHistories h
                JOIN PackagingMaterials m ON m.Id = h.PackagingMaterialId
                LEFT JOIN PackagingMaterialUnits u ON u.Id = h.PackagingMaterialUnitId;

                UPDATE m
                SET CurrentCost = COALESCE((SELECT TOP 1 h.Price
                                            FROM PackagingPriceHistories h
                                            WHERE h.PackagingMaterialId = m.Id
                                            ORDER BY h.Date DESC, h.Id DESC), 0)
                FROM PackagingMaterials m;

                UPDATE l
                SET QtyPerPiece = l.QtyPerPiece * u.BaseUnitsPerUnit,
                    PiecesPerPackagingUnit = l.PiecesPerPackagingUnit / u.BaseUnitsPerUnit
                FROM PackagingLines l
                JOIN PackagingMaterialUnits u ON u.Id = l.PackagingMaterialUnitId;
                """);

            migrationBuilder.DropTable(
                name: "PackagingMaterialUnits");

            migrationBuilder.DropIndex(
                name: "IX_PackagingPriceHistories_PackagingMaterialUnitId",
                table: "PackagingPriceHistories");

            migrationBuilder.DropIndex(
                name: "IX_PackagingLines_PackagingMaterialUnitId",
                table: "PackagingLines");

            migrationBuilder.DropColumn(
                name: "PackagingMaterialUnitId",
                table: "PackagingPriceHistories");

            migrationBuilder.DropColumn(
                name: "PackagingMaterialUnitId",
                table: "PackagingLines");

            migrationBuilder.DropColumn(
                name: "CostPerBaseUnit",
                table: "PackagingMaterials");

            migrationBuilder.RenameColumn(
                name: "BaseUnitId",
                table: "PackagingMaterials",
                newName: "StandardUnitId");

            migrationBuilder.RenameIndex(
                name: "IX_PackagingMaterials_BaseUnitId",
                table: "PackagingMaterials",
                newName: "IX_PackagingMaterials_StandardUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingMaterials_PurchaseUnitId",
                table: "PackagingMaterials",
                column: "PurchaseUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_PackagingMaterials_UnitsOfMeasure_PurchaseUnitId",
                table: "PackagingMaterials",
                column: "PurchaseUnitId",
                principalTable: "UnitsOfMeasure",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PackagingMaterials_UnitsOfMeasure_StandardUnitId",
                table: "PackagingMaterials",
                column: "StandardUnitId",
                principalTable: "UnitsOfMeasure",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
