using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A made ingredient is made from a sub-assembly, the one place its recipe is written, with add-ins of its own and its
/// weight loss on top (2026-10-27; until then a BOM of its own). Its bill of materials is read from the two.
/// </summary>
[Collection(DatabaseCollection.C)]
public class MadeIngredientLinkPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private IngredientBomLineRepository CreateBomRepository() => new(CreateDbContextFactory());

    private RawMaterialRepository CreateRepository() => new(CreateDbContextFactory());

    private SubAssemblyRepository CreateSubAssemblyRepository() => new(CreateDbContextFactory());

    /// <summary>A purchased ingredient bought by the kilogram: tests price it per kilogram (₱90 is ₱0.09 a gram).</summary>
    private async Task<int> SeedPurchasedIngredientAsync(string name)
    {
        var id = await AddIngredientAsync(name);
        await AddKilogramUnitAsync(id);
        return id;
    }

    private async Task<RawMaterial> GetIngredientAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterials.AsNoTracking().SingleAsync(i => i.Id == id, Cancellation);
    }

    /// <summary>Replaces the made ingredient's add-ins, as the Raw Materials screen's Save does, changing nothing else.</summary>
    private async Task SetAddInsAsync(int madeId, IReadOnlyList<(int IngredientId, decimal Grams)> addIns)
    {
        var made = (await CreateRepository().GetByIdAsync(madeId, Cancellation))!;
        await CreateRepository().SaveAsync(made, [], brandPrices: null, addIns, Cancellation);
    }

    [Fact]
    public async Task A_made_ingredient_is_costed_from_its_sub_assemblys_lines_less_its_weight_loss()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var waterId = await SeedPurchasedIngredientAsync("Water"); // never priced: its cost stays 0
        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 90m);

        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 1000m), (waterId, 500m)], weightLossPercent: 20m);

        // ₱90 over 1,500 g less 20% (1,200 g).
        (await GetIngredientAsync(syrupId)).CostPerBaseUnit.Should().Be(0.075m);
    }

    [Fact]
    public async Task Its_add_ins_go_into_the_batch_on_top_of_the_recipe_and_the_recipe_stays_as_it_is()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var ubeId = await SeedPurchasedIngredientAsync("Ube Flavor");
        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 90m);
        await SetPriceAsync(ubeId, await KilogramUnitIdAsync(ubeId), 900m);
        var syrupId = await AddMadeIngredientAsync("Ube Syrup", [(sugarId, 1000m)]);
        var recipeId = await RecipeIdOfAsync(syrupId);

        await SetAddInsAsync(syrupId, [(ubeId, 100m)]);

        // ₱90 + ₱90 over 1,100 g.
        (await GetIngredientAsync(syrupId)).CostPerBaseUnit.Should().Be(RawMaterialRules.StoredCost(180m / 1100m));
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == recipeId).Select(l => l.IngredientId).ToListAsync(Cancellation))
            .Should().Equal([sugarId], "an add-in is the made ingredient's, not the recipe's");
        (await dbContext.SubAssemblies.SingleAsync(s => s.Id == recipeId, Cancellation)).TotalWeight.Should().Be(1000m);
    }

    [Fact]
    public async Task GetForIngredientAsync_returns_the_recipes_lines_then_the_add_ins_with_their_components()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var waterId = await SeedPurchasedIngredientAsync("Water");
        var ubeId = await SeedPurchasedIngredientAsync("Ube Flavor");
        var syrupId = await AddMadeIngredientAsync("Ube Syrup", [(waterId, 500m), (sugarId, 1000m)]);
        await SetAddInsAsync(syrupId, [(ubeId, 20m)]);

        var lines = await CreateBomRepository().GetForIngredientAsync(syrupId, Cancellation);

        lines.Select(l => (l.ComponentIngredient!.Name, l.Grams, l.IsAddIn))
            .Should().Equal(("Water", 500m, false), ("Sugar", 1000m, false), ("Ube Flavor", 20m, true));
    }

    [Fact]
    public async Task GetAllAsync_returns_every_boms_lines_in_their_order()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var waterId = await SeedPurchasedIngredientAsync("Water");
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(waterId, 500m), (sugarId, 1000m)]);
        var caramelId = await AddMadeIngredientAsync("Caramel", [(syrupId, 800m)]);

        var lines = await CreateBomRepository().GetAllAsync(Cancellation);

        lines.Where(l => l.IngredientId == syrupId).Select(l => (l.ComponentIngredientId, l.Grams))
            .Should().Equal((waterId, 500m), (sugarId, 1000m));
        lines.Where(l => l.IngredientId == caramelId).Select(l => (l.ComponentIngredientId, l.Grams))
            .Should().Equal((syrupId, 800m));
    }

    [Fact]
    public async Task A_price_change_ripples_through_a_three_level_chain()
    {
        // sugar -> syrup -> caramel, each made ingredient made from a recipe holding the one before it.
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 90m);
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 1000m)]);
        var caramelId = await AddMadeIngredientAsync("Caramel", [(syrupId, 1000m)]);

        (await GetIngredientAsync(syrupId)).CostPerBaseUnit.Should().Be(0.09m);
        (await GetIngredientAsync(caramelId)).CostPerBaseUnit.Should().Be(0.09m);

        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 180m);

        (await GetIngredientAsync(syrupId)).CostPerBaseUnit.Should().Be(0.18m);
        (await GetIngredientAsync(caramelId)).CostPerBaseUnit.Should().Be(0.18m);
    }

    [Fact]
    public async Task Changing_the_sub_assemblys_lines_reprices_the_made_ingredient_and_what_uses_it()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var waterId = await SeedPurchasedIngredientAsync("Water");
        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 90m);
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 1000m)]);
        var toppingId = await AddRecipeAsync("Syrup Topping", [(syrupId, 100m)]);

        // Half of it water now: ₱90 over 2,000 g.
        await SetRecipeLinesAsync(syrupId, [(sugarId, 1000m), (waterId, 1000m)]);

        (await GetIngredientAsync(syrupId)).CostPerBaseUnit.Should().Be(0.045m);
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.SingleAsync(s => s.Id == toppingId, Cancellation)).TotalCost.Should().Be(4.5m);
    }

    [Fact]
    public async Task Editing_weight_loss_percent_recomputes_CostPerGram_from_the_current_recipe()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 90m);
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 1000m)]);

        var syrup = await GetIngredientAsync(syrupId);
        syrup.WeightLossPercent = 10m;
        await CreateRepository().UpdateAsync(syrup, Cancellation);

        // 1000g -> 900g output, same ₱90 total cost -> ₱0.10/g.
        (await GetIngredientAsync(syrupId)).CostPerBaseUnit.Should().Be(0.1m);
    }

    [Fact]
    public async Task Making_it_from_another_sub_assembly_reprices_it()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var waterId = await SeedPurchasedIngredientAsync("Water");
        await SetPriceAsync(sugarId, await KilogramUnitIdAsync(sugarId), 90m);
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 1000m)]);
        var thinId = await AddRecipeAsync("Thin Syrup", [(sugarId, 1000m), (waterId, 1000m)]);

        var syrup = await GetIngredientAsync(syrupId);
        syrup.SubAssemblyId = thinId;
        await CreateRepository().UpdateAsync(syrup, Cancellation);

        (await GetIngredientAsync(syrupId)).CostPerBaseUnit.Should().Be(0.045m);
    }

    [Fact]
    public async Task A_line_that_closes_a_loop_through_another_made_ingredient_is_refused_and_nothing_changes()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var bId = await AddMadeIngredientAsync("B", [(sugarId, 100m)]);
        var aId = await AddMadeIngredientAsync("A", [(bId, 100m)]); // A is made from a recipe with B in it

        var act = () => SetRecipeLinesAsync(bId, [(aId, 100m)]); // B made from A would close the loop

        await act.Should().ThrowAsync<BusinessRuleException>();
        await using var dbContext = CreateDbContext();
        var recipeId = await RecipeIdOfAsync(bId);
        (await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == recipeId).Select(l => l.IngredientId).ToListAsync(Cancellation))
            .Should().Equal([sugarId], "the refused lines roll back with their save");
    }

    [Fact]
    public async Task The_made_ingredient_as_a_line_of_its_own_sub_assembly_is_refused_by_name()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 100m)]);

        var act = () => SetRecipeLinesAsync(syrupId, [(sugarId, 100m), (syrupId, 10m)]);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("Simple Syrup is made from Simple Syrup Base, so it can't be one of Simple Syrup Base's ingredients.");
    }

    [Fact]
    public async Task An_add_in_its_recipe_already_has_is_refused()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 100m)]);

        var act = () => SetAddInsAsync(syrupId, [(sugarId, 10m)]);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Sugar is already in Simple Syrup Base*");
    }

    [Fact]
    public async Task An_add_in_that_closes_a_loop_is_refused()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 100m)]);
        var caramelId = await AddMadeIngredientAsync("Caramel", [(syrupId, 100m)]);

        var act = () => SetAddInsAsync(syrupId, [(caramelId, 10m)]);

        await act.Should().ThrowAsync<BusinessRuleException>();
        (await CreateBomRepository().GetForIngredientAsync(syrupId, Cancellation)).Should().NotContain(l => l.IsAddIn);
    }

    [Fact]
    public async Task A_made_ingredient_without_a_sub_assembly_is_refused()
    {
        var made = new RawMaterial
        {
            Name = "Simple Syrup",
            Source = RawMaterialSource.Made,
            WeightLossPercent = 0m,
            CategoryId = await MadeCategoryIdAsync(),
        };

        var act = () => CreateRepository().SaveAsync(made, [], brandPrices: null, addIns: null, Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Pick the sub-assembly it's made from.");
    }

    [Fact]
    public async Task A_product_own_sub_assembly_or_a_packaging_set_is_refused()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var (_, ownId) = await AddProductWithSubAssemblyAsync("Cheese Roll", fillingRoleId, [new RecipeLine(sugarId, 100m)]);
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 2, isPackaging: true);
        int setId;
        await using (var dbContext = CreateDbContext())
        {
            var giftSet = new SubAssembly { Name = "Gift Set", RoleId = packagingRoleId };
            dbContext.SubAssemblies.Add(giftSet);
            await dbContext.SaveChangesAsync(Cancellation);
            setId = giftSet.Id;
        }

        var categoryId = await MadeCategoryIdAsync();

        RawMaterial MadeFrom(int subAssemblyId) => new()
        {
            Name = "Simple Syrup", Source = RawMaterialSource.Made, WeightLossPercent = 0m, CategoryId = categoryId, SubAssemblyId = subAssemblyId,
        };

        var own = () => CreateRepository().SaveAsync(MadeFrom(ownId), [], brandPrices: null, addIns: null, Cancellation);
        var set = () => CreateRepository().SaveAsync(MadeFrom(setId), [], brandPrices: null, addIns: null, Cancellation);

        await own.Should().ThrowAsync<BusinessRuleException>().WithMessage("*is Cheese Roll's own, so a made ingredient can't be made from it*");
        await set.Should().ThrowAsync<BusinessRuleException>().WithMessage("Gift Set is a packaging set*");
        await using var check = CreateDbContext();
        (await check.RawMaterials.AnyAsync(m => m.Name == "Simple Syrup", Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Its_sub_assembly_cant_be_deleted_while_it_is_made_from_it()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 100m)]);

        var recipeId = await RecipeIdOfAsync(syrupId);

        var act = () => CreateSubAssemblyRepository().DeleteStandaloneAsync(recipeId, Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Simple Syrup is made from Simple Syrup Base, so it can't be deleted*");
    }

    [Fact]
    public async Task Its_sub_assembly_lists_it_as_made_from_it()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 100m)]);

        var madeFrom = await CreateSubAssemblyRepository().GetMadeIngredientsBySubAssemblyAsync(Cancellation);

        madeFrom[await RecipeIdOfAsync(syrupId)].Should().Equal(new MadeIngredientUse(syrupId, "Simple Syrup"));
    }

    [Fact]
    public async Task Deleting_a_component_ingredient_that_is_in_use_is_blocked()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 100m)]);

        await using var dbContext = CreateDbContext();
        var act = () => dbContext.RawMaterials.Where(i => i.Id == sugarId).ExecuteDeleteAsync(Cancellation);

        // ExecuteDeleteAsync issues a raw DELETE outside EF Core's change tracker, so an
        // FK violation surfaces as the driver's own PostgresException, not a DbUpdateException
        // (that wrapping only happens through SaveChangesAsync).
        await act.Should().ThrowAsync<Npgsql.PostgresException>();
    }

    [Fact]
    public async Task Deleting_the_made_ingredient_takes_its_add_ins_and_leaves_its_sub_assembly()
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var ubeId = await SeedPurchasedIngredientAsync("Ube Flavor");
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 100m)]);
        var recipeId = await RecipeIdOfAsync(syrupId);
        await SetAddInsAsync(syrupId, [(ubeId, 10m)]);

        await CreateRepository().DeleteAsync(syrupId, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.IngredientAddInLines.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.SubAssemblies.AnyAsync(s => s.Id == recipeId, Cancellation)).Should().BeTrue();
    }
}
