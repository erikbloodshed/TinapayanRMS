using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Tests.Infrastructure;

namespace TinapayanRMS.Tests;

/// <summary>
/// End-to-end tests for the reactive costing cascade (PLAN.md), proving the full chain
/// a price change ripples through: IngredientPriceHistory -> Ingredient.CostPerGram ->
/// (through the BOM tree) -> SubAssembly.TotalCost -> a product's live-read cost per piece.
/// Also proves the packaging branch (PackagingPriceHistory -> PackagingMaterial.CurrentCost
/// -> a product's live-read packaging cost) needs no propagation code of its own, since
/// nothing in it is stored redundantly.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ReactiveCostCascadeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private SubAssemblyCostPropagator CreateSubAssemblyCostPropagator() => new(CreateDbContextFactory());

    private IngredientCostPropagator CreateIngredientCostPropagator() => new(CreateDbContextFactory(), CreateSubAssemblyCostPropagator());

    private IngredientRepository CreateIngredientRepository() => new(CreateDbContextFactory(), CreateIngredientCostPropagator());

    private IngredientPriceHistoryRepository CreatePriceHistoryRepository() => new(CreateDbContextFactory(), CreateIngredientCostPropagator());

    private IngredientBomLineRepository CreateBomRepository() => new(CreateDbContextFactory(), CreateIngredientCostPropagator());

    private SubAssemblyLineRepository CreateSubAssemblyLineRepository() => new(CreateDbContextFactory());

    private PackagingPriceHistoryRepository CreatePackagingPriceHistoryRepository() => new(CreateDbContextFactory());

    private async Task<int> SeedCategoryAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        var category = new IngredientCategory { Name = name };
        dbContext.IngredientCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    private async Task<int> SeedPurchasedIngredientAsync(string name, int categoryId)
    {
        var unitId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");

        await using var dbContext = CreateDbContext();
        var ingredient = new Ingredient
        {
            Name = name,
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Purchased,
            PurchaseUnitId = unitId,
            PurchaseUnitQty = 1000m,
        };
        dbContext.Ingredients.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return ingredient.Id;
    }

    private async Task<int> SeedMadeIngredientAsync(string name, int categoryId, decimal weightLossPercent = 0m)
    {
        await using var dbContext = CreateDbContext();
        var ingredient = new Ingredient
        {
            Name = name,
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Made,
            WeightLossPercent = weightLossPercent,
        };
        dbContext.Ingredients.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return ingredient.Id;
    }

    private async Task<int> SeedRoleAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        var role = new SubAssemblyRole { Name = name, SortOrder = 1 };
        dbContext.SubAssemblyRoles.Add(role);
        await dbContext.SaveChangesAsync(Cancellation);
        return role.Id;
    }

    private async Task<int> SeedSubAssemblyAsync(string name, int roleId)
    {
        await using var dbContext = CreateDbContext();
        var subAssembly = new SubAssembly { Name = name, RoleId = roleId };
        dbContext.SubAssemblies.Add(subAssembly);
        await dbContext.SaveChangesAsync(Cancellation);
        return subAssembly.Id;
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
        var roleId = await SeedRoleAsync("Filling");
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId);

        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = sugarId, Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation); // 0.09/g
        await CreateSubAssemblyLineRepository().SetLinesAsync(subAssemblyId, [(sugarId, 1000m)], Cancellation);

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(90m);

        // Sugar's price doubles — no direct edit to the sub-assembly.
        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = sugarId, Date = new DateOnly(2026, 2, 1), Price = 180m }, Cancellation);

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(180m);
    }

    [Fact]
    public async Task A_price_change_ripples_through_an_ingredient_BOM_chain_into_a_sub_assembly()
    {
        // sugar -> syrup (made) -> Custard sub-assembly, which uses syrup directly.
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup", categoryId);
        var roleId = await SeedRoleAsync("Filling");
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId);

        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = sugarId, Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation);
        await CreateBomRepository().SetLinesAsync(syrupId, [(sugarId, 1000m)], Cancellation); // syrup: 0.09/g
        await CreateSubAssemblyLineRepository().SetLinesAsync(subAssemblyId, [(syrupId, 500m)], Cancellation); // 500g syrup @ 0.09/g -> 45

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(45m);

        // Sugar's price doubles: syrup becomes 0.18/g, which should ripple into Custard too.
        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = sugarId, Date = new DateOnly(2026, 2, 1), Price = 180m }, Cancellation);

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(90m); // 500g @ 0.18/g
    }

    [Fact]
    public async Task A_weight_loss_percent_edit_ripples_into_every_sub_assembly_using_that_ingredient()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup", categoryId, weightLossPercent: 0m);
        var roleId = await SeedRoleAsync("Filling");
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId);

        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = sugarId, Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation);
        await CreateBomRepository().SetLinesAsync(syrupId, [(sugarId, 1000m)], Cancellation); // syrup: 1000g -> 0.09/g
        await CreateSubAssemblyLineRepository().SetLinesAsync(subAssemblyId, [(syrupId, 1000m)], Cancellation); // 1000g syrup @ 0.09/g -> 90

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(90m);

        // 10% weight loss: syrup output drops to 900g for the same 90 total cost -> 0.10/g.
        await using (var dbContext = CreateDbContext())
        {
            var syrup = await dbContext.Ingredients.SingleAsync(i => i.Id == syrupId, Cancellation);
            syrup.WeightLossPercent = 10m;
            await dbContext.SaveChangesAsync(Cancellation);
        }
        await CreateIngredientRepository().UpdateAsync(new Ingredient { Id = syrupId, Name = "Simple Syrup", IngredientCategoryId = categoryId, Source = IngredientSource.Made, WeightLossPercent = 10m }, Cancellation);

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(100m); // 1000g @ 0.10/g
    }

    [Fact]
    public async Task A_price_change_recomputes_every_sub_assembly_that_uses_the_ingredient()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var roleId = await SeedRoleAsync("Filling");
        var custardId = await SeedSubAssemblyAsync("Custard", roleId);
        var caramelId = await SeedSubAssemblyAsync("Caramel Sauce", roleId);

        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = sugarId, Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation);
        await CreateSubAssemblyLineRepository().SetLinesAsync(custardId, [(sugarId, 500m)], Cancellation);
        await CreateSubAssemblyLineRepository().SetLinesAsync(caramelId, [(sugarId, 200m)], Cancellation);

        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = sugarId, Date = new DateOnly(2026, 2, 1), Price = 180m }, Cancellation); // 0.18/g

        (await GetSubAssemblyAsync(custardId)).TotalCost.Should().Be(90m); // 500 * 0.18
        (await GetSubAssemblyAsync(caramelId)).TotalCost.Should().Be(36m); // 200 * 0.18
    }

    [Fact]
    public async Task A_sub_assembly_with_multiple_ingredients_recomputes_using_every_lines_current_cost_not_just_the_changed_one()
    {
        var categoryId = await SeedCategoryAsync("Dough");
        var flourId = await SeedPurchasedIngredientAsync("Flour", categoryId);
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var roleId = await SeedRoleAsync("Dough");
        var subAssemblyId = await SeedSubAssemblyAsync("Bread Dough", roleId);

        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = flourId, Date = new DateOnly(2026, 1, 1), Price = 50m }, Cancellation); // 0.05/g
        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = sugarId, Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation); // 0.09/g
        await CreateSubAssemblyLineRepository().SetLinesAsync(subAssemblyId, [(flourId, 800m), (sugarId, 200m)], Cancellation);

        (await GetSubAssemblyAsync(subAssemblyId)).TotalCost.Should().Be(58m); // 800*0.05 + 200*0.09

        // Only flour's price changes; sugar's line cost must still use its own current cost.
        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = flourId, Date = new DateOnly(2026, 2, 1), Price = 100m }, Cancellation); // 0.10/g

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
        var roleId = await SeedRoleAsync("Filling");
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId);

        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = sugarId, Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation);
        await CreateSubAssemblyLineRepository().SetLinesAsync(subAssemblyId, [(sugarId, 100m)], Cancellation); // 100g @ 0.09/g -> 9, TotalWeight 100g

        await using var dbContext = CreateDbContext();
        var product = new Product { Name = "Ensaymada" };
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(Cancellation); // assigns product.Id before it's used as a FK below

        dbContext.ProductSubAssemblyUsages.Add(new ProductSubAssemblyUsage { ProductId = product.Id, SubAssemblyId = subAssemblyId, WeightPerPiece = 10m });
        await dbContext.SaveChangesAsync(Cancellation);

        decimal ReadUsageCostPerPiece()
        {
            var subAssembly = dbContext.SubAssemblies.AsNoTracking().Single(s => s.Id == subAssemblyId);
            // ProductRules.CalculateUsageCostPerPiece's own formula, inlined so this test
            // doesn't need a ProductsViewModel — the point is SubAssembly's own columns.
            return subAssembly.TotalCost * 10m / subAssembly.TotalWeight;
        }

        ReadUsageCostPerPiece().Should().Be(0.9m); // 9 * 10 / 100

        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = sugarId, Date = new DateOnly(2026, 2, 1), Price = 180m }, Cancellation);

        ReadUsageCostPerPiece().Should().Be(1.8m); // sugar doubled -> usage cost doubled, with zero product-level code involved
    }

    [Fact]
    public async Task The_packaging_branch_needs_no_propagation_code_a_price_change_is_visible_immediately_on_read()
    {
        int materialId;
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        await using (var dbContext = CreateDbContext())
        {
            var category = new PackagingCategory { Name = "Boxes" };
            dbContext.PackagingCategories.Add(category);
            await dbContext.SaveChangesAsync(Cancellation);

            var material = new PackagingMaterial
            {
                Name = "Box",
                PackagingCategoryId = category.Id,
                StandardUnitId = pieceId,
                PurchaseUnitId = pieceId,
                PurchaseUnitQty = 1m,
            };
            dbContext.PackagingMaterials.Add(material);
            await dbContext.SaveChangesAsync(Cancellation);
            materialId = material.Id;
        }

        await CreatePackagingPriceHistoryRepository().AddAsync(new PackagingPriceHistory { PackagingMaterialId = materialId, Date = new DateOnly(2026, 1, 1), Price = 30m }, Cancellation);

        async Task<decimal> ReadCurrentCostAsync()
        {
            await using var dbContext = CreateDbContext();
            return (await dbContext.PackagingMaterials.AsNoTracking().SingleAsync(m => m.Id == materialId, Cancellation)).CurrentCost;
        }

        (await ReadCurrentCostAsync()).Should().Be(30m);

        await CreatePackagingPriceHistoryRepository().AddAsync(new PackagingPriceHistory { PackagingMaterialId = materialId, Date = new DateOnly(2026, 2, 1), Price = 36m }, Cancellation);

        (await ReadCurrentCostAsync()).Should().Be(36m);
    }
}
