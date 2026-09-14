using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <summary>
    /// Sub-assemblies become owned by products (PLAN.md, 2026-09-14): role-tab links become sub-assembly
    /// links, every sub-assembly gets an owner, ProductTabLinks keeps only packaging links (as
    /// PackagingLinks), recipe lines lose their units (their Grams already hold the amount), and a usage
    /// stores the piece-size figure the user typed.
    ///
    /// Hand-edited: the scaffold dropped ProductTabLinks and added OwnerProductId with no owner. Down can't
    /// restore role-tab links (they stay the shared usages they were converted to) or the units lines were
    /// entered in (they come back as grams).
    /// </summary>
    public partial class ProductOwnedSubAssemblies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Role-tab links become sub-assembly links. Each one is followed to the end of its chain;
            // the linking product's own (hidden) usages in that role are replaced by the end product's,
            // at the same weights. TabKind 0 was SubAssemblyRole, 1 Packaging.
            migrationBuilder.Sql("""
                DECLARE @Ends TABLE (ProductId int, SubAssemblyRoleId int, EndProductId int);

                WITH Chain AS (
                    SELECT l.ProductId, l.SubAssemblyRoleId, l.LinkedProductId AS CurrentProductId, 1 AS Depth
                    FROM ProductTabLinks l
                    WHERE l.TabKind = 0
                    UNION ALL
                    SELECT c.ProductId, c.SubAssemblyRoleId, n.LinkedProductId, c.Depth + 1
                    FROM Chain c
                    JOIN ProductTabLinks n
                        ON n.ProductId = c.CurrentProductId AND n.TabKind = 0 AND n.SubAssemblyRoleId = c.SubAssemblyRoleId
                    WHERE c.Depth < 100
                )
                INSERT INTO @Ends (ProductId, SubAssemblyRoleId, EndProductId)
                SELECT c.ProductId, c.SubAssemblyRoleId, c.CurrentProductId
                FROM Chain c
                WHERE NOT EXISTS (
                    SELECT 1 FROM ProductTabLinks n
                    WHERE n.ProductId = c.CurrentProductId AND n.TabKind = 0 AND n.SubAssemblyRoleId = c.SubAssemblyRoleId);

                DELETE u
                FROM ProductSubAssemblyUsages u
                JOIN SubAssemblies s ON s.Id = u.SubAssemblyId
                JOIN @Ends e ON e.ProductId = u.ProductId AND e.SubAssemblyRoleId = s.RoleId;

                INSERT INTO ProductSubAssemblyUsages (ProductId, SubAssemblyId, WeightPerPiece)
                SELECT e.ProductId, u.SubAssemblyId, u.WeightPerPiece
                FROM @Ends e
                JOIN ProductSubAssemblyUsages u ON u.ProductId = e.EndProductId
                JOIN SubAssemblies s ON s.Id = u.SubAssemblyId AND s.RoleId = e.SubAssemblyRoleId;

                DELETE FROM ProductTabLinks WHERE TabKind = 0;
                """);

            // 2. A sub-assembly no product uses can't have an owner: it goes, with its lines (Cascade).
            migrationBuilder.Sql("""
                DELETE FROM SubAssemblies
                WHERE NOT EXISTS (SELECT 1 FROM ProductSubAssemblyUsages u WHERE u.SubAssemblyId = SubAssemblies.Id);
                """);

            // 3. Owners: the first product (lowest id) using each sub-assembly. The others now link it.
            migrationBuilder.AddColumn<int>(
                name: "OwnerProductId",
                table: "SubAssemblies",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE s
                SET OwnerProductId = (SELECT MIN(u.ProductId) FROM ProductSubAssemblyUsages u WHERE u.SubAssemblyId = s.Id)
                FROM SubAssemblies s;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "OwnerProductId",
                table: "SubAssemblies",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_SubAssemblies_Name",
                table: "SubAssemblies");

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblies_OwnerProductId_Name",
                table: "SubAssemblies",
                columns: new[] { "OwnerProductId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SubAssemblies_Products_OwnerProductId",
                table: "SubAssemblies",
                column: "OwnerProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // 4. What's left of ProductTabLinks is packaging links: renamed, keeping their rows.
            migrationBuilder.DropForeignKey(
                name: "FK_ProductTabLinks_SubAssemblyRoles_SubAssemblyRoleId",
                table: "ProductTabLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductTabLinks_Products_LinkedProductId",
                table: "ProductTabLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductTabLinks_Products_ProductId",
                table: "ProductTabLinks");

            migrationBuilder.DropIndex(
                name: "IX_ProductTabLinks_ProductId",
                table: "ProductTabLinks");

            migrationBuilder.DropIndex(
                name: "IX_ProductTabLinks_ProductId_SubAssemblyRoleId",
                table: "ProductTabLinks");

            migrationBuilder.DropIndex(
                name: "IX_ProductTabLinks_SubAssemblyRoleId",
                table: "ProductTabLinks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductTabLinks",
                table: "ProductTabLinks");

            migrationBuilder.DropColumn(
                name: "SubAssemblyRoleId",
                table: "ProductTabLinks");

            migrationBuilder.DropColumn(
                name: "TabKind",
                table: "ProductTabLinks");

            migrationBuilder.RenameTable(
                name: "ProductTabLinks",
                newName: "PackagingLinks");

            migrationBuilder.RenameIndex(
                name: "IX_ProductTabLinks_LinkedProductId",
                table: "PackagingLinks",
                newName: "IX_PackagingLinks_LinkedProductId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PackagingLinks",
                table: "PackagingLinks",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingLinks_ProductId",
                table: "PackagingLinks",
                column: "ProductId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PackagingLinks_Products_LinkedProductId",
                table: "PackagingLinks",
                column: "LinkedProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PackagingLinks_Products_ProductId",
                table: "PackagingLinks",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // 5. Recipe lines are in grams only. Grams already holds every line's amount.
            migrationBuilder.DropForeignKey(
                name: "FK_IngredientBomLines_RawMaterialUnits_UnitId",
                table: "IngredientBomLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SubAssemblyLines_RawMaterialUnits_UnitId",
                table: "SubAssemblyLines");

            migrationBuilder.DropIndex(
                name: "IX_SubAssemblyLines_UnitId",
                table: "SubAssemblyLines");

            migrationBuilder.DropIndex(
                name: "IX_IngredientBomLines_UnitId",
                table: "IngredientBomLines");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "SubAssemblyLines");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "SubAssemblyLines");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "IngredientBomLines");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "IngredientBomLines");

            // 6. A usage stores the figure the user typed. Every existing one typed its weight per piece
            // (AuthoredField 0), so the weight carries straight over.
            migrationBuilder.RenameColumn(
                name: "WeightPerPiece",
                table: "ProductSubAssemblyUsages",
                newName: "AuthoredValue");

            migrationBuilder.AddColumn<int>(
                name: "AuthoredField",
                table: "ProductSubAssemblyUsages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductSubAssemblyUsages_AuthoredValue",
                table: "ProductSubAssemblyUsages",
                sql: "[AuthoredValue] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 6. Back to a stored weight per piece: a typed yield becomes the weight it came to.
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductSubAssemblyUsages_AuthoredValue",
                table: "ProductSubAssemblyUsages");

            migrationBuilder.Sql("""
                UPDATE u
                SET AuthoredValue = s.TotalWeight / u.AuthoredValue
                FROM ProductSubAssemblyUsages u
                JOIN SubAssemblies s ON s.Id = u.SubAssemblyId
                WHERE u.AuthoredField = 1 AND s.TotalWeight > 0;
                """);

            migrationBuilder.DropColumn(
                name: "AuthoredField",
                table: "ProductSubAssemblyUsages");

            migrationBuilder.RenameColumn(
                name: "AuthoredValue",
                table: "ProductSubAssemblyUsages",
                newName: "WeightPerPiece");

            // 5. Lines come back entered in grams, with no unit.
            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "SubAssemblyLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "UnitId",
                table: "SubAssemblyLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "IngredientBomLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "UnitId",
                table: "IngredientBomLines",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE SubAssemblyLines SET Quantity = Grams;
                UPDATE IngredientBomLines SET Quantity = Grams;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblyLines_UnitId",
                table: "SubAssemblyLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientBomLines_UnitId",
                table: "IngredientBomLines",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_IngredientBomLines_RawMaterialUnits_UnitId",
                table: "IngredientBomLines",
                column: "UnitId",
                principalTable: "RawMaterialUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubAssemblyLines_RawMaterialUnits_UnitId",
                table: "SubAssemblyLines",
                column: "UnitId",
                principalTable: "RawMaterialUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // 4. PackagingLinks back to ProductTabLinks, every row a Packaging tab (TabKind 1).
            migrationBuilder.DropForeignKey(
                name: "FK_PackagingLinks_Products_LinkedProductId",
                table: "PackagingLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_PackagingLinks_Products_ProductId",
                table: "PackagingLinks");

            migrationBuilder.DropIndex(
                name: "IX_PackagingLinks_ProductId",
                table: "PackagingLinks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PackagingLinks",
                table: "PackagingLinks");

            migrationBuilder.RenameTable(
                name: "PackagingLinks",
                newName: "ProductTabLinks");

            migrationBuilder.RenameIndex(
                name: "IX_PackagingLinks_LinkedProductId",
                table: "ProductTabLinks",
                newName: "IX_ProductTabLinks_LinkedProductId");

            migrationBuilder.AddColumn<int>(
                name: "TabKind",
                table: "ProductTabLinks",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SubAssemblyRoleId",
                table: "ProductTabLinks",
                type: "int",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductTabLinks",
                table: "ProductTabLinks",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_ProductTabLinks_ProductId",
                table: "ProductTabLinks",
                column: "ProductId",
                unique: true,
                filter: "[SubAssemblyRoleId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductTabLinks_ProductId_SubAssemblyRoleId",
                table: "ProductTabLinks",
                columns: new[] { "ProductId", "SubAssemblyRoleId" },
                unique: true,
                filter: "[SubAssemblyRoleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductTabLinks_SubAssemblyRoleId",
                table: "ProductTabLinks",
                column: "SubAssemblyRoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductTabLinks_Products_LinkedProductId",
                table: "ProductTabLinks",
                column: "LinkedProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductTabLinks_Products_ProductId",
                table: "ProductTabLinks",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductTabLinks_SubAssemblyRoles_SubAssemblyRoleId",
                table: "ProductTabLinks",
                column: "SubAssemblyRoleId",
                principalTable: "SubAssemblyRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // 3. No owners, and names unique across every product again: a name shared by several
            // owners keeps it on its first sub-assembly, and the others get their owner's name added.
            migrationBuilder.DropForeignKey(
                name: "FK_SubAssemblies_Products_OwnerProductId",
                table: "SubAssemblies");

            migrationBuilder.DropIndex(
                name: "IX_SubAssemblies_OwnerProductId_Name",
                table: "SubAssemblies");

            migrationBuilder.Sql("""
                UPDATE s
                SET Name = LEFT(s.Name + N' (' + p.Name + N')', 100)
                FROM SubAssemblies s
                JOIN Products p ON p.Id = s.OwnerProductId
                WHERE EXISTS (SELECT 1 FROM SubAssemblies o WHERE o.Name = s.Name AND o.Id < s.Id);
                """);

            migrationBuilder.DropColumn(
                name: "OwnerProductId",
                table: "SubAssemblies");

            migrationBuilder.CreateIndex(
                name: "IX_SubAssemblies_Name",
                table: "SubAssemblies",
                column: "Name",
                unique: true);

            // 2 and 1 can't be undone: deleted unused sub-assemblies stay deleted, and converted role-tab
            // links stay the shared usages they became.
        }
    }
}
