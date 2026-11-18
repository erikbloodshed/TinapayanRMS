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
/// The database's backstops for the figures a read divides by or adds up, since one packaging
/// line stored at 0 would fail Reports, Print and the Recipe API for every product. The rules refuse these first, in better
/// words, so these tests write past the repositories, as an edit outside the app would.
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class QuantityCheckConstraintTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    [Arguments(PackagingLineAuthoredField.QtyPerUnit, 0, 5)]
    [Arguments(PackagingLineAuthoredField.QtyPerUnit, -1, 5)]
    [Arguments(PackagingLineAuthoredField.UnitsPerPackagingUnit, 5, 0)]
    [Arguments(PackagingLineAuthoredField.UnitsPerPackagingUnit, 5, -1)]
    public async Task A_packaging_lines_typed_quantity_has_to_be_positive(PackagingLineAuthoredField authored, int qtyPerUnit, int unitsPerPackagingUnit)
    {
        var line = await NewPackagingLineAsync(authored, qtyPerUnit, unitsPerPackagingUnit);

        await SavingShouldBeRefusedByAsync("CK_PackagingLines_AuthoredQuantity", line);
    }

    [Test]
    [Arguments(PackagingLineAuthoredField.UnitsPerPackagingUnit, 0, 30000)]
    [Arguments(PackagingLineAuthoredField.QtyPerUnit, 30000, 0)]
    public async Task A_packaging_lines_worked_out_figure_can_round_to_0_at_the_columns_places(PackagingLineAuthoredField authored, int qtyPerUnit, int unitsPerPackagingUnit)
    {
        var line = await NewPackagingLineAsync(authored, qtyPerUnit, unitsPerPackagingUnit);
        await using var dbContext = CreateDbContext();
        dbContext.PackagingLines.Add(line);

        var save = () => dbContext.SaveChangesAsync(Cancellation);

        await save.Should().NotThrowAsync("1 / 30,000 is 0.0000 to 4 places, and it's the typed figure that's read");
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task A_sub_assembly_lines_grams_have_to_be_positive(int grams)
    {
        var ingredientId = await AddIngredientAsync("Flour", 0.05m);
        var recipeId = await AddEmptyRecipeAsync();

        await SavingShouldBeRefusedByAsync(
            "CK_SubAssemblyLines_Grams", new SubAssemblyLine { SubAssemblyId = recipeId, IngredientId = ingredientId, Grams = grams });
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
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

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task A_made_ingredients_add_ins_grams_have_to_be_positive(int grams)
    {
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var ubeId = await AddIngredientAsync("Ube", 0.2m);

        await SavingShouldBeRefusedByAsync(
            "CK_IngredientAddInLines_Grams", new IngredientAddInLine { IngredientId = flourId, AddInIngredientId = ubeId, Grams = grams });
    }

    [Test]
    [Arguments(100)]
    [Arguments(150)]
    [Arguments(-1)]
    public async Task A_made_ingredients_weight_loss_is_at_least_0_and_under_100(int weightLossPercent)
    {
        // At 100% the recipe would make nothing, and the ingredient would cost nothing a gram.
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var syrupId = await AddMadeIngredientAsync("Syrup", [(flourId, 1000m)]);
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(m => m.Id == syrupId, Cancellation)).WeightLossPercent = weightLossPercent;

        var save = () => dbContext.SaveChangesAsync(Cancellation);

        (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>().WithMessage("*CK_RawMaterials_WeightLossPercent*");
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task A_units_multiplier_has_to_be_positive(int baseUnitsPerUnit)
    {
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");

        await SavingShouldBeRefusedByAsync(
            "CK_RawMaterialUnits_BaseUnitsPerUnit",
            new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = bagId, BaseUnitsPerUnit = baseUnitsPerUnit });
    }

    [Test]
    [Arguments(nameof(CostingParameters.LaborPercent))]
    [Arguments(nameof(CostingParameters.OverheadPercent))]
    [Arguments(nameof(CostingParameters.AdministrativePercent))]
    [Arguments(nameof(CostingParameters.MarginPercent))]
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

    [Test]
    public async Task The_shops_vat_cant_be_negative() =>
        await SavingShouldBeRefusedByAsync("CK_CostingDefaults_VatPercent", new CostingDefaults { VatPercent = -100m });

    private async Task SavingShouldBeRefusedByAsync(string constraint, object row)
    {
        await using var dbContext = CreateDbContext();
        dbContext.Add(row);

        var save = () => dbContext.SaveChangesAsync(Cancellation);

        (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>().WithMessage($"*{constraint}*");
    }
}
