using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// The database's backstops for the figures a read divides by or adds up (2026-11-03, found by a code review: one packaging
/// line stored at 0 failed Reports, Print and the Recipe API for every product). The rules refuse these first, in better
/// words, so these tests write past the repositories, as an edit outside the app would.
/// </summary>
[Collection(DatabaseCollection.C)]
public class QuantityCheckConstraintTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Theory]
    [InlineData(PackagingLineAuthoredField.QtyPerUnit, 0, 5)]
    [InlineData(PackagingLineAuthoredField.QtyPerUnit, -1, 5)]
    [InlineData(PackagingLineAuthoredField.UnitsPerPackagingUnit, 5, 0)]
    [InlineData(PackagingLineAuthoredField.UnitsPerPackagingUnit, 5, -1)]
    public async Task A_packaging_lines_typed_quantity_has_to_be_positive(PackagingLineAuthoredField authored, int qtyPerUnit, int unitsPerPackagingUnit)
    {
        var line = await NewPackagingLineAsync(authored, qtyPerUnit, unitsPerPackagingUnit);

        await SavingShouldBeRefusedByAsync("CK_PackagingLines_AuthoredQuantity", line);
    }

    [Theory]
    [InlineData(PackagingLineAuthoredField.UnitsPerPackagingUnit, 0, 30000)]
    [InlineData(PackagingLineAuthoredField.QtyPerUnit, 30000, 0)]
    public async Task A_packaging_lines_worked_out_figure_can_round_to_0_at_the_columns_places(PackagingLineAuthoredField authored, int qtyPerUnit, int unitsPerPackagingUnit)
    {
        var line = await NewPackagingLineAsync(authored, qtyPerUnit, unitsPerPackagingUnit);
        await using var dbContext = CreateDbContext();
        dbContext.PackagingLines.Add(line);

        var save = () => dbContext.SaveChangesAsync(Cancellation);

        await save.Should().NotThrowAsync("1 / 30,000 is 0.0000 to 4 places, and it's the typed figure that's read");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_sub_assembly_lines_grams_have_to_be_positive(int grams)
    {
        var ingredientId = await AddIngredientAsync("Flour", 0.05m);
        var recipeId = await AddEmptyRecipeAsync();

        await SavingShouldBeRefusedByAsync(
            "CK_SubAssemblyLines_Grams", new SubAssemblyLine { SubAssemblyId = recipeId, IngredientId = ingredientId, Grams = grams });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_products_add_ins_grams_have_to_be_positive(int grams)
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var ubeId = await AddIngredientAsync("Ube", 0.2m);
        await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);
        int usageId;
        await using (var dbContext = CreateDbContext())
        {
            usageId = await dbContext.ProductSubAssemblyUsages.Select(u => u.Id).SingleAsync(Cancellation);
        }

        await SavingShouldBeRefusedByAsync("CK_UsageAddInLines_Grams", new UsageAddInLine { UsageId = usageId, IngredientId = ubeId, Grams = grams });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_made_ingredients_add_ins_grams_have_to_be_positive(int grams)
    {
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var ubeId = await AddIngredientAsync("Ube", 0.2m);

        await SavingShouldBeRefusedByAsync(
            "CK_IngredientAddInLines_Grams", new IngredientAddInLine { IngredientId = flourId, AddInIngredientId = ubeId, Grams = grams });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_units_multiplier_has_to_be_positive(int baseUnitsPerUnit)
    {
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");

        await SavingShouldBeRefusedByAsync(
            "CK_RawMaterialUnits_BaseUnitsPerUnit",
            new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = bagId, BaseUnitsPerUnit = baseUnitsPerUnit });
    }

    [Theory]
    [InlineData(nameof(CostingParameters.LaborPercent))]
    [InlineData(nameof(CostingParameters.OverheadPercent))]
    [InlineData(nameof(CostingParameters.AdministrativePercent))]
    [InlineData(nameof(CostingParameters.MarginPercent))]
    [InlineData(nameof(CostingParameters.VatPercent))]
    public async Task A_costing_percent_cant_be_negative(string percent)
    {
        var productId = await AddProductAsync("Pandesal");
        var costing = new CostingParameters { ProductId = productId };
        typeof(CostingParameters).GetProperty(percent)!.SetValue(costing, -100m);

        await SavingShouldBeRefusedByAsync("CK_CostingParameters_Percents", costing);
    }

    private async Task<PackagingLine> NewPackagingLineAsync(PackagingLineAuthoredField authored, decimal qtyPerUnit, decimal unitsPerPackagingUnit)
    {
        var productId = await AddProductAsync("Pandesal");
        var categoryId = await AddPackagingCategoryAsync();
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        await using var dbContext = CreateDbContext();
        var box = new RawMaterial { Type = RawMaterialType.Packaging, Name = "Box", CategoryId = categoryId, BaseUnitId = pieceId };
        dbContext.RawMaterials.Add(box);
        await dbContext.SaveChangesAsync(Cancellation);
        return new PackagingLine
        {
            ProductId = productId,
            PackagingMaterialId = box.Id,
            AuthoredField = authored,
            QtyPerUnit = qtyPerUnit,
            UnitsPerPackagingUnit = unitsPerPackagingUnit,
        };
    }

    private async Task SavingShouldBeRefusedByAsync(string constraint, object row)
    {
        await using var dbContext = CreateDbContext();
        dbContext.Add(row);

        var save = () => dbContext.SaveChangesAsync(Cancellation);

        (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>().WithMessage($"*{constraint}*");
    }
}
