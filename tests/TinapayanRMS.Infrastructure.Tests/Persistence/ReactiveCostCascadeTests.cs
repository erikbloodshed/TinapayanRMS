using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// End-to-end tests for the reactive costing cascade (PLAN.md), proving the full chain
/// a price change ripples through: an ingredient's price history -> its stored costs ->
/// (through the BOM tree) the made ingredients' stored costs -> a sub-assembly's cost and a
/// product's cost per piece, both worked out from their lines on read (SubAssemblyTotalsAsync).
/// Also proves the packaging branch (a packaging material's price history -> its CostPerBaseUnit
/// -> a product's live-read packaging cost) needs no propagation code of its own, since
/// nothing in it is stored redundantly.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class ReactiveCostCascadeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private RawMaterialRepository CreateIngredientRepository() => new(CreateDbContextFactory());


    private async Task<int> SeedCategoryAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = name };
        dbContext.RawMaterialCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    private async Task<int> SeedPurchasedIngredientAsync(string name, int categoryId)
    {
        await using var dbContext = CreateDbContext();
        var ingredient = new RawMaterial
        {
            Name = name,
            CategoryId = categoryId,
            Source = RawMaterialSource.Purchased,
        };
        dbContext.RawMaterials.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);

        // Bought by the kilogram: tests price it per kilogram (₱90 is ₱0.09 a gram).
        await AddKilogramUnitAsync(ingredient.Id);
        return ingredient.Id;
    }

    /// <summary>A sub-assembly of <paramref name="grams"/> of one ingredient, in a product of its own (a sub-assembly always has an owner).</summary>
    private async Task<int> SeedSubAssemblyAsync(string name, int roleId, int ingredientId, decimal grams)
    {
        var (_, subAssemblyId) = await AddProductWithSubAssemblyAsync($"{name} product", roleId, [(ingredientId, grams)]);
        return subAssemblyId;
    }

    [Test]
    public async Task A_price_change_on_an_ingredient_used_directly_in_a_sub_assembly_reaches_its_cost()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var roleId = await AddRoleAsync("Filling");

        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 90m); // 0.09/g
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId, sugarId, 1000m);

        (await SubAssemblyTotalsAsync(subAssemblyId)).TotalCost.Should().Be(90m);

        // Sugar's price doubles — no direct edit to the sub-assembly.
        await SetPriceAndRecalculateBufferAsync(sugarId, await KilogramUnitIdAsync(sugarId), 180m);

        (await SubAssemblyTotalsAsync(subAssemblyId)).TotalCost.Should().Be(180m);
    }

    [Test]
    public async Task A_price_change_ripples_through_an_ingredient_BOM_chain_into_a_sub_assembly()
    {
        // sugar -> syrup (made) -> Custard sub-assembly, which uses syrup directly.
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var roleId = await AddRoleAsync("Filling");

        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 90m);
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 1000m)]); // syrup: 0.09/g
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId, syrupId, 500m); // 500g syrup @ 0.09/g -> 45

        (await SubAssemblyTotalsAsync(subAssemblyId)).TotalCost.Should().Be(45m);

        // Sugar's price doubles: syrup becomes 0.18/g, which should ripple into Custard too.
        await SetPriceAndRecalculateBufferAsync(sugarId, await KilogramUnitIdAsync(sugarId), 180m);

        (await SubAssemblyTotalsAsync(subAssemblyId)).TotalCost.Should().Be(90m); // 500g @ 0.18/g
    }

    [Test]
    public async Task A_weight_loss_percent_edit_ripples_into_every_sub_assembly_using_that_ingredient()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var madeCategoryId = await MadeCategoryIdAsync();
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var roleId = await AddRoleAsync("Filling");

        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 90m);
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 1000m)]); // syrup: 1000g -> 0.09/g
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId, syrupId, 1000m); // 1000g syrup @ 0.09/g -> 90

        (await SubAssemblyTotalsAsync(subAssemblyId)).TotalCost.Should().Be(90m);

        // 10% weight loss: syrup output drops to 900g for the same 90 total cost -> 0.10/g.
        await using (var dbContext = CreateDbContext())
        {
            var syrup = await dbContext.RawMaterials.SingleAsync(i => i.Id == syrupId, Cancellation);
            syrup.WeightLossPercent = 10m;
            await dbContext.SaveChangesAsync(Cancellation);
        }
        await CreateIngredientRepository().UpdateAsync(
            new RawMaterial { Id = syrupId, Name = "Simple Syrup", CategoryId = madeCategoryId, Source = RawMaterialSource.Made, WeightLossPercent = 10m, SubAssemblyId = await RecipeIdOfAsync(syrupId) },
            Cancellation);

        (await SubAssemblyTotalsAsync(subAssemblyId)).TotalCost.Should().Be(100m); // 1000g @ 0.10/g
    }

    [Test]
    public async Task A_line_edit_on_the_sub_assembly_a_made_ingredient_is_made_from_ripples_into_every_sub_assembly_using_it()
    {
        // sugar -> Syrup Base (sub-assembly) -> syrup (made) -> Custard sub-assembly. Syrup Base gains water.
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var waterId = await SeedPurchasedIngredientAsync("Water", categoryId);
        var roleId = await AddRoleAsync("Filling");
        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 90m);
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 1000m)]);
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId, syrupId, 500m); // 500g syrup @ 0.09/g -> 45

        await SetRecipeLinesAsync(syrupId, [(sugarId, 1000m), (waterId, 1000m)]); // syrup: ₱90 over 2,000 g -> 0.045/g

        (await SubAssemblyTotalsAsync(subAssemblyId)).TotalCost.Should().Be(22.5m);
    }

    [Test]
    public async Task A_price_change_on_a_made_ingredients_add_in_ripples_into_every_sub_assembly_using_it_and_leaves_its_own_sub_assembly_alone()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var vanillaId = await SeedPurchasedIngredientAsync("Vanilla", categoryId);
        var roleId = await AddRoleAsync("Filling");
        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 90m);
        await SetPriceAsync(vanillaId, await KilogramUnitIdAsync(vanillaId), 1000m);
        var syrupId = await AddMadeIngredientAsync("Vanilla Syrup", [(sugarId, 900m)]);
        var rawMaterials = CreateIngredientRepository();
        await rawMaterials.SaveAsync((await rawMaterials.GetByIdAsync(syrupId, Cancellation))!, [], brandPrices: null, [(vanillaId, 100m)], Cancellation);
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId, syrupId, 1000m); // ₱81 + ₱100 over 1,000 g -> 181
        var baseId = await RecipeIdOfAsync(syrupId);
        var baseCost = (await SubAssemblyTotalsAsync(baseId)).TotalCost;

        (await SubAssemblyTotalsAsync(subAssemblyId)).TotalCost.Should().Be(181m);

        await SetPriceAndRecalculateBufferAsync(vanillaId, await KilogramUnitIdAsync(vanillaId), 2000m);

        (await SubAssemblyTotalsAsync(subAssemblyId)).TotalCost.Should().Be(281m);
        (await SubAssemblyTotalsAsync(baseId)).TotalCost.Should().Be(baseCost, "an add-in is the made ingredient's, never its sub-assembly's");
    }

    [Test]
    public async Task A_price_change_reaches_every_sub_assembly_that_uses_the_ingredient()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var roleId = await AddRoleAsync("Filling");

        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 90m);
        var custardId = await SeedSubAssemblyAsync("Custard", roleId, sugarId, 500m);
        var caramelId = await SeedSubAssemblyAsync("Caramel Sauce", roleId, sugarId, 200m);

        await SetPriceAndRecalculateBufferAsync(sugarId, await KilogramUnitIdAsync(sugarId), 180m); // 0.18/g

        (await SubAssemblyTotalsAsync(custardId)).TotalCost.Should().Be(90m); // 500 * 0.18
        (await SubAssemblyTotalsAsync(caramelId)).TotalCost.Should().Be(36m); // 200 * 0.18
    }

    [Test]
    public async Task A_sub_assembly_with_multiple_ingredients_is_costed_at_every_lines_current_cost_not_just_the_changed_one()
    {
        var categoryId = await SeedCategoryAsync("Dough");
        var flourId = await SeedPurchasedIngredientAsync("Flour", categoryId);
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var roleId = await AddRoleAsync("Dough");

        await SetPriceAsync(flourId, await KilogramUnitIdAsync(flourId), 50m); // 0.05/g
        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 90m); // 0.09/g
        var (_, subAssemblyId) = await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 800m), (sugarId, 200m)]);

        (await SubAssemblyTotalsAsync(subAssemblyId)).TotalCost.Should().Be(58m); // 800*0.05 + 200*0.09

        // Only flour's price changes; sugar's line cost must still use its own current cost.
        await SetPriceAndRecalculateBufferAsync(flourId, await KilogramUnitIdAsync(flourId), 100m); // 0.10/g

        (await SubAssemblyTotalsAsync(subAssemblyId)).TotalCost.Should().Be(98m); // 800*0.10 + 200*0.09
    }

    [Test]
    public async Task A_products_live_read_cost_reflects_an_ingredient_price_change_with_no_product_level_propagation()
    {
        // Proves a product needs no propagation code of its own: its sections are costed from
        // their lines as they're read, and its cost with them, never stored (Phase 8), so it
        // follows a new price as soon as the ingredient's buffered cost does.
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var roleId = await AddRoleAsync("Filling");

        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 90m);
        // 100g @ 0.09/g -> 9, TotalWeight 100g, used at 10g a piece.
        var (productId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(sugarId, 100m)], weightPerPiece: 10m);

        await using var dbContext = CreateDbContext();

        async Task<decimal> ReadUsageCostPerPieceAsync()
        {
            var usage = await dbContext.ProductSubAssemblyUsages.AsNoTracking().SingleAsync(u => u.ProductId == productId, Cancellation);
            var (totalWeight, totalCost) = await SubAssemblyTotalsAsync(usage.SubAssemblyId);
            var (weightPerPiece, _) = ProductRules.ResolvePieceSize(usage.AuthoredField, usage.AuthoredValue, totalWeight);
            return ProductRules.CalculateUsageCostPerPiece(totalCost, weightPerPiece, totalWeight);
        }

        (await ReadUsageCostPerPieceAsync()).Should().Be(0.9m); // 9 * 10 / 100

        await SetPriceAndRecalculateBufferAsync(sugarId, await KilogramUnitIdAsync(sugarId), 180m);

        (await ReadUsageCostPerPieceAsync()).Should().Be(1.8m); // sugar doubled -> usage cost doubled, with zero product-level code involved
    }

    [Test]
    public async Task A_price_change_reaches_a_linked_usages_add_in_with_nothing_stored_to_propagate()
    {
        // Add-ins are the third branch that needs no propagation code. They hang off the usage, and their
        // cost is read off the ingredient, so nothing derived about them is stored — and the sub-assembly
        // they sit on top of keeps its own lines, since it belongs to another product.
        var categoryId = await SeedCategoryAsync("Fillings");
        var flourId = await SeedPurchasedIngredientAsync("Flour", categoryId);
        var ubeId = await SeedPurchasedIngredientAsync("Ube Flavour", categoryId);
        var roleId = await AddRoleAsync("Dough");

        await SetPriceAsync(flourId, await KilogramUnitIdAsync(flourId), 50m);
        await SetPriceAsync(ubeId, await KilogramUnitIdAsync(ubeId), 800m);

        // Cream Loaf owns a 1,000 g / ₱50 dough; Cream Loaf Ube links it and stirs in 20 g of ube (₱16).
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var linkerId = await AddProductAsync("Cream Loaf Ube");
        await SaveRecipeAsync(linkerId, new RecipeDraft(
            [],
            [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m) { AddIns = [(ubeId, 20m)] }]));

        await using var dbContext = CreateDbContext();

        async Task<decimal> ReadUsageCostPerPieceAsync()
        {
            var usage = await dbContext.ProductSubAssemblyUsages.AsNoTracking().SingleAsync(u => u.ProductId == linkerId, Cancellation);
            var addIns = await dbContext.UsageAddInLines.AsNoTracking().Include(l => l.Ingredient).Where(l => l.UsageId == usage.Id).ToListAsync(Cancellation);
            var (ownWeight, ownCost) = await SubAssemblyTotalsAsync(usage.SubAssemblyId);
            var (totalWeight, totalCost) = ProductRules.CalculateEffectiveBatch(
                ownWeight, ownCost, [.. addIns.Select(l => (l.Grams, l.Ingredient!.BufferedCostPerBaseUnit))]);
            var (weightPerPiece, _) = ProductRules.ResolvePieceSize(usage.AuthoredField, usage.AuthoredValue, totalWeight);
            return ProductRules.CalculateUsageCostPerPiece(totalCost, weightPerPiece, totalWeight);
        }

        // A 1,020 g / ₱66 effective batch at 30 g a piece.
        (await ReadUsageCostPerPieceAsync()).Should().Be(66m * 30m / 1020m);

        await SetPriceAndRecalculateBufferAsync(ubeId, await KilogramUnitIdAsync(ubeId), 1600m);

        // ₱82 now: the ube doubled to ₱32, with no add-in-level propagation code anywhere.
        (await ReadUsageCostPerPieceAsync()).Should().Be(82m * 30m / 1020m);

        (await SubAssemblyTotalsAsync(doughId)).Should().Be((1000m, 50m), "the ube is the linking product's own; the dough is Cream Loaf's");
    }

    [Test]
    public async Task The_packaging_branch_needs_no_propagation_code_a_price_change_is_visible_immediately_on_read()
    {
        int materialId;
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        await using (var dbContext = CreateDbContext())
        {
            var category = new RawMaterialCategory { Name = "Boxes" };
            dbContext.RawMaterialCategories.Add(category);
            await dbContext.SaveChangesAsync(Cancellation);

            var material = new RawMaterial
            {
                Type = RawMaterialType.Packaging,
                Name = "Box",
                CategoryId = category.Id,
                BaseUnitId = pieceId,
            };
            dbContext.RawMaterials.Add(material);
            await dbContext.SaveChangesAsync(Cancellation);
            materialId = material.Id;
        }

        await SetPriceAsync(materialId, null, 30m);

        async Task<decimal> ReadCostPerBaseUnitAsync()
        {
            await using var dbContext = CreateDbContext();
            return (await dbContext.RawMaterials.AsNoTracking().SingleAsync(m => m.Id == materialId, Cancellation)).CostPerBaseUnit;
        }

        (await ReadCostPerBaseUnitAsync()).Should().Be(30m);

        await SetPriceAsync(materialId, null, 36m);

        (await ReadCostPerBaseUnitAsync()).Should().Be(36m);
    }
}
