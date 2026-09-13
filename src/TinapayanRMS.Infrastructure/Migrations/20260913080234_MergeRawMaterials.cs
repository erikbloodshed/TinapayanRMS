using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <summary>
    /// Merges ingredients and packaging materials into one RawMaterials table (with a Type), and their
    /// categories, alternative units and price histories into one table each.
    ///
    /// Hand-edited: the scaffold dropped the old tables before creating the new ones. The new tables
    /// are created first, the data is copied across (<see cref="CopyIntoRawMaterialsSql"/>), and only
    /// then are the old tables dropped. Ingredient rows keep their ids, so recipe and BOM lines and
    /// their units still point at them; packaging rows get new ids after them, and packaging lines
    /// follow. Down splits the tables again, keeping every id, since packaging ids no longer collide.
    /// </summary>
    public partial class MergeRawMaterials : Migration
    {
        /// <summary>
        /// One batch, so the id maps (table variables) last through every step. A packaging category
        /// joins the ingredient category of the same name, or is added. A packaging material named like
        /// an ingredient gets " (packaging)" appended, since names are now unique across both.
        /// </summary>
        private const string CopyIntoRawMaterialsSql = """
            SET IDENTITY_INSERT [RawMaterialCategories] ON;
            INSERT INTO [RawMaterialCategories] ([Id], [Name])
            SELECT [Id], [Name] FROM [IngredientCategories];
            SET IDENTITY_INSERT [RawMaterialCategories] OFF;

            INSERT INTO [RawMaterialCategories] ([Name])
            SELECT p.[Name] FROM [PackagingCategories] p
            WHERE NOT EXISTS (SELECT 1 FROM [RawMaterialCategories] c WHERE c.[Name] = p.[Name]);

            SET IDENTITY_INSERT [RawMaterials] ON;
            INSERT INTO [RawMaterials] ([Id], [Name], [Type], [CategoryId], [Source], [WeightLossPercent], [BaseUnitId], [CostPerBaseUnit])
            SELECT [Id], [Name], N'Ingredient', [IngredientCategoryId], [Source], [WeightLossPercent], NULL, [CostPerGram]
            FROM [Ingredients];
            SET IDENTITY_INSERT [RawMaterials] OFF;

            DECLARE @MaterialIds TABLE ([OldId] int PRIMARY KEY, [NewId] int NOT NULL);
            MERGE INTO [RawMaterials] AS target
            USING (
                SELECT p.[Id], p.[BaseUnitId], p.[CostPerBaseUnit], c.[Id] AS [CategoryId],
                    CASE WHEN EXISTS (SELECT 1 FROM [Ingredients] i WHERE i.[Name] = p.[Name])
                        THEN LEFT(p.[Name], 88) + N' (packaging)'
                        ELSE p.[Name] END AS [Name]
                FROM [PackagingMaterials] p
                JOIN [PackagingCategories] pc ON pc.[Id] = p.[PackagingCategoryId]
                JOIN [RawMaterialCategories] c ON c.[Name] = pc.[Name]
            ) AS source
            ON 1 = 0
            WHEN NOT MATCHED THEN
                INSERT ([Name], [Type], [CategoryId], [Source], [WeightLossPercent], [BaseUnitId], [CostPerBaseUnit])
                VALUES (source.[Name], N'Packaging', source.[CategoryId], N'Purchased', NULL, source.[BaseUnitId], source.[CostPerBaseUnit])
            OUTPUT source.[Id], inserted.[Id] INTO @MaterialIds ([OldId], [NewId]);

            SET IDENTITY_INSERT [RawMaterialUnits] ON;
            INSERT INTO [RawMaterialUnits] ([Id], [RawMaterialId], [UnitOfMeasureId], [BaseUnitsPerUnit], [Description])
            SELECT [Id], [IngredientId], [UnitOfMeasureId], [GramsPerUnit], [Description] FROM [IngredientUnits];
            SET IDENTITY_INSERT [RawMaterialUnits] OFF;

            DECLARE @UnitIds TABLE ([OldId] int PRIMARY KEY, [NewId] int NOT NULL);
            MERGE INTO [RawMaterialUnits] AS target
            USING (
                SELECT u.[Id], m.[NewId] AS [RawMaterialId], u.[UnitOfMeasureId], u.[BaseUnitsPerUnit], u.[Description]
                FROM [PackagingMaterialUnits] u
                JOIN @MaterialIds m ON m.[OldId] = u.[PackagingMaterialId]
            ) AS source
            ON 1 = 0
            WHEN NOT MATCHED THEN
                INSERT ([RawMaterialId], [UnitOfMeasureId], [BaseUnitsPerUnit], [Description])
                VALUES (source.[RawMaterialId], source.[UnitOfMeasureId], source.[BaseUnitsPerUnit], source.[Description])
            OUTPUT source.[Id], inserted.[Id] INTO @UnitIds ([OldId], [NewId]);

            SET IDENTITY_INSERT [RawMaterialPriceHistories] ON;
            INSERT INTO [RawMaterialPriceHistories] ([Id], [RawMaterialId], [Date], [UnitId], [Price])
            SELECT [Id], [IngredientId], [Date], [IngredientUnitId], [Price] FROM [IngredientPriceHistories];
            SET IDENTITY_INSERT [RawMaterialPriceHistories] OFF;

            INSERT INTO [RawMaterialPriceHistories] ([RawMaterialId], [Date], [UnitId], [Price])
            SELECT m.[NewId], h.[Date], u.[NewId], h.[Price]
            FROM [PackagingPriceHistories] h
            JOIN @MaterialIds m ON m.[OldId] = h.[PackagingMaterialId]
            LEFT JOIN @UnitIds u ON u.[OldId] = h.[PackagingMaterialUnitId];

            UPDATE l SET l.[PackagingMaterialId] = m.[NewId], l.[UnitId] = u.[NewId]
            FROM [PackagingLines] l
            JOIN @MaterialIds m ON m.[OldId] = l.[PackagingMaterialId]
            LEFT JOIN @UnitIds u ON u.[OldId] = l.[UnitId];
            """;

        /// <summary>
        /// Splits the merged tables back by type, every id kept. Each category goes to both old
        /// category tables, since either kind of material may use it.
        /// </summary>
        private const string CopyOutOfRawMaterialsSql = """
            SET IDENTITY_INSERT [IngredientCategories] ON;
            INSERT INTO [IngredientCategories] ([Id], [Name])
            SELECT [Id], [Name] FROM [RawMaterialCategories];
            SET IDENTITY_INSERT [IngredientCategories] OFF;

            SET IDENTITY_INSERT [PackagingCategories] ON;
            INSERT INTO [PackagingCategories] ([Id], [Name])
            SELECT [Id], [Name] FROM [RawMaterialCategories];
            SET IDENTITY_INSERT [PackagingCategories] OFF;

            SET IDENTITY_INSERT [Ingredients] ON;
            INSERT INTO [Ingredients] ([Id], [Name], [IngredientCategoryId], [Source], [WeightLossPercent], [CostPerGram])
            SELECT [Id], [Name], [CategoryId], [Source], [WeightLossPercent], [CostPerBaseUnit]
            FROM [RawMaterials] WHERE [Type] = N'Ingredient';
            SET IDENTITY_INSERT [Ingredients] OFF;

            SET IDENTITY_INSERT [PackagingMaterials] ON;
            INSERT INTO [PackagingMaterials] ([Id], [Name], [PackagingCategoryId], [BaseUnitId], [CostPerBaseUnit])
            SELECT [Id], [Name], [CategoryId], [BaseUnitId], [CostPerBaseUnit]
            FROM [RawMaterials] WHERE [Type] = N'Packaging';
            SET IDENTITY_INSERT [PackagingMaterials] OFF;

            SET IDENTITY_INSERT [IngredientUnits] ON;
            INSERT INTO [IngredientUnits] ([Id], [IngredientId], [UnitOfMeasureId], [GramsPerUnit], [Description])
            SELECT u.[Id], u.[RawMaterialId], u.[UnitOfMeasureId], u.[BaseUnitsPerUnit], u.[Description]
            FROM [RawMaterialUnits] u JOIN [RawMaterials] m ON m.[Id] = u.[RawMaterialId]
            WHERE m.[Type] = N'Ingredient';
            SET IDENTITY_INSERT [IngredientUnits] OFF;

            SET IDENTITY_INSERT [PackagingMaterialUnits] ON;
            INSERT INTO [PackagingMaterialUnits] ([Id], [PackagingMaterialId], [UnitOfMeasureId], [BaseUnitsPerUnit], [Description])
            SELECT u.[Id], u.[RawMaterialId], u.[UnitOfMeasureId], u.[BaseUnitsPerUnit], u.[Description]
            FROM [RawMaterialUnits] u JOIN [RawMaterials] m ON m.[Id] = u.[RawMaterialId]
            WHERE m.[Type] = N'Packaging';
            SET IDENTITY_INSERT [PackagingMaterialUnits] OFF;

            SET IDENTITY_INSERT [IngredientPriceHistories] ON;
            INSERT INTO [IngredientPriceHistories] ([Id], [IngredientId], [Date], [IngredientUnitId], [Price])
            SELECT h.[Id], h.[RawMaterialId], h.[Date], h.[UnitId], h.[Price]
            FROM [RawMaterialPriceHistories] h JOIN [RawMaterials] m ON m.[Id] = h.[RawMaterialId]
            WHERE m.[Type] = N'Ingredient';
            SET IDENTITY_INSERT [IngredientPriceHistories] OFF;

            SET IDENTITY_INSERT [PackagingPriceHistories] ON;
            INSERT INTO [PackagingPriceHistories] ([Id], [PackagingMaterialId], [Date], [PackagingMaterialUnitId], [Price])
            SELECT h.[Id], h.[RawMaterialId], h.[Date], h.[UnitId], h.[Price]
            FROM [RawMaterialPriceHistories] h JOIN [RawMaterials] m ON m.[Id] = h.[RawMaterialId]
            WHERE m.[Type] = N'Packaging';
            SET IDENTITY_INSERT [PackagingPriceHistories] OFF;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IngredientBomLines_IngredientUnits_IngredientUnitId",
                table: "IngredientBomLines");

            migrationBuilder.DropForeignKey(
                name: "FK_IngredientBomLines_Ingredients_ComponentIngredientId",
                table: "IngredientBomLines");

            migrationBuilder.DropForeignKey(
                name: "FK_IngredientBomLines_Ingredients_IngredientId",
                table: "IngredientBomLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PackagingLines_PackagingMaterialUnits_PackagingMaterialUnitId",
                table: "PackagingLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PackagingLines_PackagingMaterials_PackagingMaterialId",
                table: "PackagingLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SubAssemblyLines_IngredientUnits_IngredientUnitId",
                table: "SubAssemblyLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SubAssemblyLines_Ingredients_IngredientId",
                table: "SubAssemblyLines");

            migrationBuilder.RenameColumn(
                name: "IngredientUnitId",
                table: "SubAssemblyLines",
                newName: "UnitId");

            migrationBuilder.RenameIndex(
                name: "IX_SubAssemblyLines_IngredientUnitId",
                table: "SubAssemblyLines",
                newName: "IX_SubAssemblyLines_UnitId");

            migrationBuilder.RenameColumn(
                name: "PackagingMaterialUnitId",
                table: "PackagingLines",
                newName: "UnitId");

            migrationBuilder.RenameIndex(
                name: "IX_PackagingLines_PackagingMaterialUnitId",
                table: "PackagingLines",
                newName: "IX_PackagingLines_UnitId");

            migrationBuilder.RenameColumn(
                name: "IngredientUnitId",
                table: "IngredientBomLines",
                newName: "UnitId");

            migrationBuilder.RenameIndex(
                name: "IX_IngredientBomLines_IngredientUnitId",
                table: "IngredientBomLines",
                newName: "IX_IngredientBomLines_UnitId");

            migrationBuilder.CreateTable(
                name: "RawMaterialCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterialCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawMaterials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WeightLossPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    BaseUnitId = table.Column<int>(type: "int", nullable: true),
                    CostPerBaseUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterials", x => x.Id);
                    table.CheckConstraint("CK_RawMaterials_BaseUnitMatchesType", "([Type] = 'Ingredient' AND [BaseUnitId] IS NULL) OR ([Type] = 'Packaging' AND [BaseUnitId] IS NOT NULL)");
                    table.CheckConstraint("CK_RawMaterials_OnlyIngredientsAreMade", "[Type] = 'Ingredient' OR [Source] = 'Purchased'");
                    table.ForeignKey(
                        name: "FK_RawMaterials_RawMaterialCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "RawMaterialCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawMaterials_UnitsOfMeasure_BaseUnitId",
                        column: x => x.BaseUnitId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawMaterialUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RawMaterialId = table.Column<int>(type: "int", nullable: false),
                    UnitOfMeasureId = table.Column<int>(type: "int", nullable: false),
                    BaseUnitsPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterialUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawMaterialUnits_RawMaterials_RawMaterialId",
                        column: x => x.RawMaterialId,
                        principalTable: "RawMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RawMaterialUnits_UnitsOfMeasure_UnitOfMeasureId",
                        column: x => x.UnitOfMeasureId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawMaterialPriceHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RawMaterialId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterialPriceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawMaterialPriceHistories_RawMaterialUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "RawMaterialUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawMaterialPriceHistories_RawMaterials_RawMaterialId",
                        column: x => x.RawMaterialId,
                        principalTable: "RawMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialCategories_Name",
                table: "RawMaterialCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialPriceHistories_RawMaterialId_Date",
                table: "RawMaterialPriceHistories",
                columns: new[] { "RawMaterialId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialPriceHistories_UnitId",
                table: "RawMaterialPriceHistories",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterials_BaseUnitId",
                table: "RawMaterials",
                column: "BaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterials_CategoryId",
                table: "RawMaterials",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterials_Name",
                table: "RawMaterials",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialUnits_RawMaterialId_UnitOfMeasureId",
                table: "RawMaterialUnits",
                columns: new[] { "RawMaterialId", "UnitOfMeasureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialUnits_UnitOfMeasureId",
                table: "RawMaterialUnits",
                column: "UnitOfMeasureId");

            migrationBuilder.Sql(CopyIntoRawMaterialsSql);

            migrationBuilder.DropTable(
                name: "IngredientPriceHistories");

            migrationBuilder.DropTable(
                name: "PackagingPriceHistories");

            migrationBuilder.DropTable(
                name: "IngredientUnits");

            migrationBuilder.DropTable(
                name: "PackagingMaterialUnits");

            migrationBuilder.DropTable(
                name: "Ingredients");

            migrationBuilder.DropTable(
                name: "PackagingMaterials");

            migrationBuilder.DropTable(
                name: "IngredientCategories");

            migrationBuilder.DropTable(
                name: "PackagingCategories");

            migrationBuilder.AddForeignKey(
                name: "FK_IngredientBomLines_RawMaterialUnits_UnitId",
                table: "IngredientBomLines",
                column: "UnitId",
                principalTable: "RawMaterialUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IngredientBomLines_RawMaterials_ComponentIngredientId",
                table: "IngredientBomLines",
                column: "ComponentIngredientId",
                principalTable: "RawMaterials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IngredientBomLines_RawMaterials_IngredientId",
                table: "IngredientBomLines",
                column: "IngredientId",
                principalTable: "RawMaterials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PackagingLines_RawMaterialUnits_UnitId",
                table: "PackagingLines",
                column: "UnitId",
                principalTable: "RawMaterialUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PackagingLines_RawMaterials_PackagingMaterialId",
                table: "PackagingLines",
                column: "PackagingMaterialId",
                principalTable: "RawMaterials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubAssemblyLines_RawMaterialUnits_UnitId",
                table: "SubAssemblyLines",
                column: "UnitId",
                principalTable: "RawMaterialUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubAssemblyLines_RawMaterials_IngredientId",
                table: "SubAssemblyLines",
                column: "IngredientId",
                principalTable: "RawMaterials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IngredientBomLines_RawMaterialUnits_UnitId",
                table: "IngredientBomLines");

            migrationBuilder.DropForeignKey(
                name: "FK_IngredientBomLines_RawMaterials_ComponentIngredientId",
                table: "IngredientBomLines");

            migrationBuilder.DropForeignKey(
                name: "FK_IngredientBomLines_RawMaterials_IngredientId",
                table: "IngredientBomLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PackagingLines_RawMaterialUnits_UnitId",
                table: "PackagingLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PackagingLines_RawMaterials_PackagingMaterialId",
                table: "PackagingLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SubAssemblyLines_RawMaterialUnits_UnitId",
                table: "SubAssemblyLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SubAssemblyLines_RawMaterials_IngredientId",
                table: "SubAssemblyLines");

            migrationBuilder.RenameColumn(
                name: "UnitId",
                table: "SubAssemblyLines",
                newName: "IngredientUnitId");

            migrationBuilder.RenameIndex(
                name: "IX_SubAssemblyLines_UnitId",
                table: "SubAssemblyLines",
                newName: "IX_SubAssemblyLines_IngredientUnitId");

            migrationBuilder.RenameColumn(
                name: "UnitId",
                table: "PackagingLines",
                newName: "PackagingMaterialUnitId");

            migrationBuilder.RenameIndex(
                name: "IX_PackagingLines_UnitId",
                table: "PackagingLines",
                newName: "IX_PackagingLines_PackagingMaterialUnitId");

            migrationBuilder.RenameColumn(
                name: "UnitId",
                table: "IngredientBomLines",
                newName: "IngredientUnitId");

            migrationBuilder.RenameIndex(
                name: "IX_IngredientBomLines_UnitId",
                table: "IngredientBomLines",
                newName: "IX_IngredientBomLines_IngredientUnitId");

            migrationBuilder.CreateTable(
                name: "IngredientCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PackagingCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackagingCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ingredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IngredientCategoryId = table.Column<int>(type: "int", nullable: false),
                    CostPerGram = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WeightLossPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ingredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ingredients_IngredientCategories_IngredientCategoryId",
                        column: x => x.IngredientCategoryId,
                        principalTable: "IngredientCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PackagingMaterials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BaseUnitId = table.Column<int>(type: "int", nullable: false),
                    PackagingCategoryId = table.Column<int>(type: "int", nullable: false),
                    CostPerBaseUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackagingMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackagingMaterials_PackagingCategories_PackagingCategoryId",
                        column: x => x.PackagingCategoryId,
                        principalTable: "PackagingCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackagingMaterials_UnitsOfMeasure_BaseUnitId",
                        column: x => x.BaseUnitId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IngredientUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IngredientId = table.Column<int>(type: "int", nullable: false),
                    UnitOfMeasureId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    GramsPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
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

            migrationBuilder.CreateTable(
                name: "IngredientPriceHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IngredientId = table.Column<int>(type: "int", nullable: false),
                    IngredientUnitId = table.Column<int>(type: "int", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientPriceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientPriceHistories_IngredientUnits_IngredientUnitId",
                        column: x => x.IngredientUnitId,
                        principalTable: "IngredientUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IngredientPriceHistories_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackagingPriceHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackagingMaterialId = table.Column<int>(type: "int", nullable: false),
                    PackagingMaterialUnitId = table.Column<int>(type: "int", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackagingPriceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackagingPriceHistories_PackagingMaterialUnits_PackagingMaterialUnitId",
                        column: x => x.PackagingMaterialUnitId,
                        principalTable: "PackagingMaterialUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackagingPriceHistories_PackagingMaterials_PackagingMaterialId",
                        column: x => x.PackagingMaterialId,
                        principalTable: "PackagingMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IngredientCategories_Name",
                table: "IngredientCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngredientPriceHistories_IngredientId_Date",
                table: "IngredientPriceHistories",
                columns: new[] { "IngredientId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_IngredientPriceHistories_IngredientUnitId",
                table: "IngredientPriceHistories",
                column: "IngredientUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_IngredientCategoryId",
                table: "Ingredients",
                column: "IngredientCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_Name",
                table: "Ingredients",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngredientUnits_IngredientId_UnitOfMeasureId",
                table: "IngredientUnits",
                columns: new[] { "IngredientId", "UnitOfMeasureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngredientUnits_UnitOfMeasureId",
                table: "IngredientUnits",
                column: "UnitOfMeasureId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingCategories_Name",
                table: "PackagingCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackagingMaterials_BaseUnitId",
                table: "PackagingMaterials",
                column: "BaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingMaterials_Name",
                table: "PackagingMaterials",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackagingMaterials_PackagingCategoryId",
                table: "PackagingMaterials",
                column: "PackagingCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingMaterialUnits_PackagingMaterialId_UnitOfMeasureId",
                table: "PackagingMaterialUnits",
                columns: new[] { "PackagingMaterialId", "UnitOfMeasureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackagingMaterialUnits_UnitOfMeasureId",
                table: "PackagingMaterialUnits",
                column: "UnitOfMeasureId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingPriceHistories_PackagingMaterialId_Date",
                table: "PackagingPriceHistories",
                columns: new[] { "PackagingMaterialId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_PackagingPriceHistories_PackagingMaterialUnitId",
                table: "PackagingPriceHistories",
                column: "PackagingMaterialUnitId");

            migrationBuilder.Sql(CopyOutOfRawMaterialsSql);

            migrationBuilder.DropTable(
                name: "RawMaterialPriceHistories");

            migrationBuilder.DropTable(
                name: "RawMaterialUnits");

            migrationBuilder.DropTable(
                name: "RawMaterials");

            migrationBuilder.DropTable(
                name: "RawMaterialCategories");

            migrationBuilder.AddForeignKey(
                name: "FK_IngredientBomLines_IngredientUnits_IngredientUnitId",
                table: "IngredientBomLines",
                column: "IngredientUnitId",
                principalTable: "IngredientUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IngredientBomLines_Ingredients_ComponentIngredientId",
                table: "IngredientBomLines",
                column: "ComponentIngredientId",
                principalTable: "Ingredients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IngredientBomLines_Ingredients_IngredientId",
                table: "IngredientBomLines",
                column: "IngredientId",
                principalTable: "Ingredients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PackagingLines_PackagingMaterialUnits_PackagingMaterialUnitId",
                table: "PackagingLines",
                column: "PackagingMaterialUnitId",
                principalTable: "PackagingMaterialUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PackagingLines_PackagingMaterials_PackagingMaterialId",
                table: "PackagingLines",
                column: "PackagingMaterialId",
                principalTable: "PackagingMaterials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubAssemblyLines_IngredientUnits_IngredientUnitId",
                table: "SubAssemblyLines",
                column: "IngredientUnitId",
                principalTable: "IngredientUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubAssemblyLines_Ingredients_IngredientId",
                table: "SubAssemblyLines",
                column: "IngredientId",
                principalTable: "Ingredients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
