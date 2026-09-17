using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

/// <summary>
/// End-to-end tests for the reactive costing cascade (PLAN.md), proving the full chain
/// a price change ripples through: an ingredient's price history -> its CostPerBaseUnit ->
/// (through the BOM tree) -> SubAssembly.TotalCost -> a product's live-read cost per piece.
/// Also proves the packaging branch (a packaging material's price history -> its CostPerBaseUnit
/// -> a product's live-read packaging cost) needs no propagation code of its own, since
/// nothing in it is stored redundantly.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ReactiveCostCascadeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private SubAssemblyCostPropagator CreateSubAssemblyCostPropagator() => new(CreateDbContextFactory());

    private IngredientCostPropagator CreateIngredientCostPropagator() => new(CreateDbContextFactory(), CreateSubAssemblyCostPropagator());

    private RawMaterialRepository CreateIngredientRepository() => new(CreateDbContextFactory(), CreateIngredientCostPropagator());

    private RawMaterialPriceHistoryRepository CreatePriceHistoryRepository() => new(CreateDbContextFactory(), CreateIngredientCostPropagator());

    private IngredientBomLineRepository CreateBomRepository() => new(CreateDbContextFactory(), CreateIngredientCostPropagator());

    private RawMaterialPriceHistoryRepository CreatePackagingPriceHistoryRepository() => new(CreateDbContextFactory(), CreateIngredientCostPropagator());

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

    private async Task<int> SeedMadeIngredientAsync(string name, int categoryId, decimal weightLossPercent = 0m)
    {
        await using var dbContext = CreateDbContext();
        var ingredient = new RawMaterial
        {
            Name = name,
            CategoryId = categoryId,
            Source = RawMaterialSource.Made,
            WeightLossPercent = weightLossPercent,
        };
        dbContext.RawMaterials.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return ingredient.Id;
    }

    /// <summary>A sub-assembly of <paramref name="grams"/> of one ingredient, in a product of its own (a sub-assembly always has an owner).</summary>
    private async Task<int> SeedSubAssemblyAsync(string name, int roleId, int ingredientId, decimal grams)
    {
        var (_, subAssemblyId) = await AddProductWithSubAssemblyAsync($"{name} product", roleId, [(ingredientId, grams)]);
        return subAssemblyId;
    }

    private async Task<SubAssembly> GetSubAssemblyAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == id, Cancellation);
    }

    [Fact]
    public async Task A_price_change_on_an_ingredient_used_directly_in_a_sub_assembly_recomputes_its_TotalCost()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var roleId = await AddRoleAsync("Filling");

        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = sugarId, UnitId = await KilogramUnitIdAsync(sugarId), Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation); // 0.09/g
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId, sugarId, 1000m);

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(90m);

        // Sugar's price doubles — no direct edit to the sub-assembly.
        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = sugarId, UnitId = await KilogramUnitIdAsync(sugarId), Date = new DateOnly(2026, 2, 1), Price = 180m }, Cancellation);

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(180m);
    }

    [Fact]
    public async Task A_price_change_ripples_through_an_ingredient_BOM_chain_into_a_sub_assembly()
    {
        // sugar -> syrup (made) -> Custard sub-assembly, which uses syrup directly.
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup", categoryId);
        var roleId = await AddRoleAsync("Filling");

        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = sugarId, UnitId = await KilogramUnitIdAsync(sugarId), Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation);
        await CreateBomRepository().SetLinesAsync(syrupId, [(sugarId, 1000m)], Cancellation); // syrup: 0.09/g
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId, syrupId, 500m); // 500g syrup @ 0.09/g -> 45

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(45m);

        // Sugar's price doubles: syrup becomes 0.18/g, which should ripple into Custard too.
        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = sugarId, UnitId = await KilogramUnitIdAsync(sugarId), Date = new DateOnly(2026, 2, 1), Price = 180m }, Cancellation);

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(90m); // 500g @ 0.18/g
    }

    [Fact]
    public async Task A_weight_loss_percent_edit_ripples_into_every_sub_assembly_using_that_ingredient()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup", categoryId, weightLossPercent: 0m);
        var roleId = await AddRoleAsync("Filling");

        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = sugarId, UnitId = await KilogramUnitIdAsync(sugarId), Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation);
        await CreateBomRepository().SetLinesAsync(syrupId, [(sugarId, 1000m)], Cancellation); // syrup: 1000g -> 0.09/g
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId, syrupId, 1000m); // 1000g syrup @ 0.09/g -> 90

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(90m);

        // 10% weight loss: syrup output drops to 900g for the same 90 total cost -> 0.10/g.
        await using (var dbContext = CreateDbContext())
        {
            var syrup = await dbContext.RawMaterials.SingleAsync(i => i.Id == syrupId, Cancellation);
            syrup.WeightLossPercent = 10m;
            await dbContext.SaveChangesAsync(Cancellation);
        }
        await CreateIngredientRepository().UpdateAsync(new RawMaterial { Id = syrupId, Name = "Simple Syrup", CategoryId = categoryId, Source = RawMaterialSource.Made, WeightLossPercent = 10m }, Cancellation);

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(100m); // 1000g @ 0.10/g
    }

    [Fact]
    public async Task A_price_change_recomputes_every_sub_assembly_that_uses_the_ingredient()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var roleId = await AddRoleAsync("Filling");

        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = sugarId, UnitId = await KilogramUnitIdAsync(sugarId), Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation);
        var custardId = await SeedSubAssemblyAsync("Custard", roleId, sugarId, 500m);
        var caramelId = await SeedSubAssemblyAsync("Caramel Sauce", roleId, sugarId, 200m);

        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = sugarId, UnitId = await KilogramUnitIdAsync(sugarId), Date = new DateOnly(2026, 2, 1), Price = 180m }, Cancellation); // 0.18/g

        (await GetSubAssemblyAsync(custardId)).TotalCost.Should().Be(90m); // 500 * 0.18
        (await GetSubAssemblyAsync(caramelId)).TotalCost.Should().Be(36m); // 200 * 0.18
    }

    [Fact]
    public async Task A_sub_assembly_with_multiple_ingredients_recomputes_using_every_lines_current_cost_not_just_the_changed_one()
    {
        var categoryId = await SeedCategoryAsync("Dough");
        var flourId = await SeedPurchasedIngredientAsync("Flour", categoryId);
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var roleId = await AddRoleAsync("Dough");

        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = flourId, UnitId = await KilogramUnitIdAsync(flourId), Date = new DateOnly(2026, 1, 1), Price = 50m }, Cancellation); // 0.05/g
        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = sugarId, UnitId = await KilogramUnitIdAsync(sugarId), Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation); // 0.09/g
        var (_, subAssemblyId) = await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 800m), (sugarId, 200m)]);

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(58m); // 800*0.05 + 200*0.09

        // Only flour's price changes; sugar's line cost must still use its own current cost.
        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = flourId, UnitId = await KilogramUnitIdAsync(flourId), Date = new DateOnly(2026, 2, 1), Price = 100m }, Cancellation); // 0.10/g

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(98m); // 800*0.10 + 200*0.09
    }

    [Fact]
    public async Task A_products_live_read_cost_reflects_an_ingredient_price_change_with_no_product_level_propagation()
    {
        // Proves Product needs no propagation code of its own: as long as SubAssembly.
        // TotalCost is kept fresh, a product's cost — always computed on read, never
        // stored (Phase 8) — is automatically correct.
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var roleId = await AddRoleAsync("Filling");

        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = sugarId, UnitId = await KilogramUnitIdAsync(sugarId), Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation);
        // 100g @ 0.09/g -> 9, TotalWeight 100g, used at 10g a piece.
        var (productId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(sugarId, 100m)], weightPerPiece: 10m);

        await using var dbContext = CreateDbContext();

        decimal ReadUsageCostPerPiece()
        {
            var usage = dbContext.ProductSubAssemblyUsages.AsNoTracking().Include(u => u.SubAssembly).Single(u => u.ProductId == productId);
            var (weightPerPiece, _) = ProductRules.ResolvePieceSize(usage.AuthoredField, usage.AuthoredValue, usage.SubAssembly!.TotalWeight);
            return ProductRules.CalculateUsageCostPerPiece(usage.SubAssembly.TotalCost, weightPerPiece, usage.SubAssembly.TotalWeight);
        }

        ReadUsageCostPerPiece().Should().Be(0.9m); // 9 * 10 / 100

        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = sugarId, UnitId = await KilogramUnitIdAsync(sugarId), Date = new DateOnly(2026, 2, 1), Price = 180m }, Cancellation);

        ReadUsageCostPerPiece().Should().Be(1.8m); // sugar doubled -> usage cost doubled, with zero product-level code involved
    }

    [Fact]
    public async Task A_price_change_reaches_a_linked_usages_add_in_with_nothing_stored_to_propagate()
    {
        // Add-ins are the third branch that needs no propagation code. They hang off the usage, and their
        // cost is read off the ingredient, so nothing derived about them is stored — and the sub-assembly
        // they sit on top of must not move, since it belongs to another product.
        var categoryId = await SeedCategoryAsync("Fillings");
        var flourId = await SeedPurchasedIngredientAsync("Flour", categoryId);
        var ubeId = await SeedPurchasedIngredientAsync("Ube Flavour", categoryId);
        var roleId = await AddRoleAsync("Dough");

        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = flourId, UnitId = await KilogramUnitIdAsync(flourId), Date = new DateOnly(2026, 1, 1), Price = 50m }, Cancellation);
        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = ubeId, UnitId = await KilogramUnitIdAsync(ubeId), Date = new DateOnly(2026, 1, 1), Price = 800m }, Cancellation);

        // Cream Loaf owns a 1,000 g / ₱50 dough; Cream Loaf Ube links it and stirs in 20 g of ube (₱16).
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var linkerId = await AddProductAsync("Cream Loaf Ube");
        await SaveRecipeAsync(linkerId, new RecipeDraft(
            [],
            [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m) { AddIns = [(ubeId, 20m)] }]));

        await using var dbContext = CreateDbContext();

        decimal ReadUsageCostPerPiece()
        {
            var usage = dbContext.ProductSubAssemblyUsages.AsNoTracking().Include(u => u.SubAssembly).Single(u => u.ProductId == linkerId);
            var addIns = dbContext.UsageAddInLines.AsNoTracking().Include(l => l.Ingredient).Where(l => l.UsageId == usage.Id).ToList();
            var (totalWeight, totalCost) = ProductRules.CalculateEffectiveBatch(
                usage.SubAssembly!.TotalWeight,
                usage.SubAssembly.TotalCost,
                [.. addIns.Select(l => (l.Grams, l.Ingredient!.BufferedCostPerBaseUnit))]);
            var (weightPerPiece, _) = ProductRules.ResolvePieceSize(usage.AuthoredField, usage.AuthoredValue, totalWeight);
            return ProductRules.CalculateUsageCostPerPiece(totalCost, weightPerPiece, totalWeight);
        }

        // A 1,020 g / ₱66 effective batch at 30 g a piece.
        ReadUsageCostPerPiece().Should().Be(66m * 30m / 1020m);

        await CreatePriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = ubeId, UnitId = await KilogramUnitIdAsync(ubeId), Date = new DateOnly(2026, 2, 1), Price = 1600m }, Cancellation);

        // ₱82 now: the ube doubled to ₱32, with no add-in-level propagation code anywhere.
        ReadUsageCostPerPiece().Should().Be(82m * 30m / 1020m);

        var dough = await GetSubAssemblyAsync(doughId);
        dough.TotalWeight.Should().Be(1000m, "the ube is the linking product's own; the dough is Cream Loaf's");
        dough.TotalCost.Should().Be(50m);
    }

    [Fact]
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

        await CreatePackagingPriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = materialId, Date = new DateOnly(2026, 1, 1), Price = 30m }, Cancellation);

        async Task<decimal> ReadCostPerBaseUnitAsync()
        {
            await using var dbContext = CreateDbContext();
            return (await dbContext.RawMaterials.AsNoTracking().SingleAsync(m => m.Id == materialId, Cancellation)).CostPerBaseUnit;
        }

        (await ReadCostPerBaseUnitAsync()).Should().Be(30m);

        await CreatePackagingPriceHistoryRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = materialId, Date = new DateOnly(2026, 2, 1), Price = 36m }, Cancellation);

        (await ReadCostPerBaseUnitAsync()).Should().Be(36m);
    }
}
