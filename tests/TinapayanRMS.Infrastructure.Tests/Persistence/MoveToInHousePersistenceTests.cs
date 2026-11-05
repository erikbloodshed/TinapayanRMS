using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// Move to In-House: a product's Save moves a section of its own to the Sub-Assemblies screen and makes a made ingredient
/// from it (NewSubAssemblyDraft.ToMadeIngredient), and the Sub-Assemblies screen's Make In-House Ingredient makes one from a
/// recipe there (ISubAssemblyRepository.AddMadeIngredientAsync). The made ingredient is made from the
/// sub-assembly, which stays as it is, in a Made category with no weight loss, so nothing's cost moves. Refused, either
/// writes nothing.
/// </summary>
[Collection(DatabaseCollection.D)]
public class MoveToInHousePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private static NewSubAssemblyDraft Section(
        int roleId, IReadOnlyList<RecipeLine> lines, int id = 0, string? sectionName = null, MadeIngredientDraft? made = null) =>
        new(roleId, lines, UsageAuthoredField.WeightPerPiece, 30m, sectionName) { Id = id, Placement = made is null ? new SectionPlacement.ByRole() : new SectionPlacement.MoveToInHouse(made) };

    /// <summary>Butter at ₱0.50 a gram (₱0.55 buffered) and Sugar at ₱0.10 (₱0.11), in a Cheese Roll topping of the product's own.</summary>
    private async Task<(int ProductId, int ToppingRoleId, int ToppingId, IReadOnlyList<RecipeLine> Lines)> SeedToppingAsync()
    {
        var toppingRoleId = await AddRoleAsync("Topping", isProductOwned: true);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.5m, bufferedCostPerGram: 0.55m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.1m, bufferedCostPerGram: 0.11m);
        var productId = await AddProductAsync("Cheese Roll");
        IReadOnlyList<RecipeLine> lines = [new(butterId, 300m, IsFormulaBase: true), new(sugarId, 200m, AuthoredBakersPercent: 66.6667m)];
        await SaveRecipeAsync(productId, new RecipeDraft([Section(toppingRoleId, lines)], []));
        return (productId, toppingRoleId, await SubAssemblyIdAsync(productId, toppingRoleId), lines);
    }

    private async Task<SubAssembly> SubAssemblyAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == id, Cancellation);
    }

    private async Task<List<SubAssemblyLine>> LinesAsync(int subAssemblyId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblyLines.AsNoTracking().Where(l => l.SubAssemblyId == subAssemblyId).OrderBy(l => l.SortOrder).ToListAsync(Cancellation);
    }

    private async Task<int> CategoryIdAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterialCategories.Where(c => c.Name == name).Select(c => c.Id).SingleAsync(Cancellation);
    }

    [Fact]
    public async Task An_own_section_moves_to_Sub_Assemblies_as_it_is_and_a_made_ingredient_is_made_from_it()
    {
        var (productId, toppingRoleId, toppingId, lines) = await SeedToppingAsync();
        var inHouseId = await MadeCategoryIdAsync();
        var before = await SubAssemblyTotalsAsync(toppingId);

        await SaveRecipeAsync(productId, new RecipeDraft(
            [Section(toppingRoleId, lines, id: toppingId, made: new MadeIngredientDraft(" Butter Icing ", inHouseId))], []));

        await using var dbContext = CreateDbContext();
        var icing = await dbContext.RawMaterials.AsNoTracking().SingleAsync(m => m.Name == "Butter Icing", Cancellation);
        (icing.Type, icing.Source, icing.WeightLossPercent, icing.CategoryId, icing.SubAssemblyId)
            .Should().Be((RawMaterialType.Ingredient, RawMaterialSource.Made, 0m, inHouseId, (int?)toppingId));
        icing.CostPerBaseUnit.Should().Be(0.34m, "₱150 of butter and ₱20 of sugar over the 500 g they make");
        icing.BufferedCostPerBaseUnit.Should().Be(0.374m, "₱165 and ₱22 buffered, over the same 500 g");

        var topping = await SubAssemblyAsync(toppingId);
        (await LinesAsync(toppingId)).Select(l => (l.IngredientId, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))
            .Should().Equal((lines[0].IngredientId, 300m, true, (decimal?)null), (lines[1].IngredientId, 200m, false, 66.6667m));
        (await SubAssemblyTotalsAsync(toppingId)).Should().Be(before, "its lines stay as they were");
        topping.OwnerProductId.Should().BeNull("it's on the Sub-Assemblies screen now, where a made ingredient can be made from it");
        topping.Name.Should().Be("Cheese Roll Topping");
        var usage = await dbContext.ProductSubAssemblyUsages.SingleAsync(Cancellation);
        (usage.SubAssemblyId, usage.AuthoredValue).Should().Be((toppingId, 30m), "the product links it at the same piece size");
    }

    [Fact]
    public async Task A_section_made_in_the_same_save_can_move_to_In_House()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.2m);
        var productId = await AddProductAsync("Ube Bun");

        await SaveRecipeAsync(productId, new RecipeDraft(
            [Section(fillingRoleId, [(ubeId, 400m)], sectionName: "Ube", made: new MadeIngredientDraft("Ube Filling", await MadeCategoryIdAsync()))], []));

        await using var dbContext = CreateDbContext();
        var filling = await dbContext.RawMaterials.AsNoTracking().SingleAsync(m => m.Name == "Ube Filling", Cancellation);
        var section = await SubAssemblyAsync(await SubAssemblyIdAsync(productId, fillingRoleId));
        section.Name.Should().Be("Ube Bun Filling: Ube");
        section.OwnerProductId.Should().BeNull();
        (await LinesAsync(section.Id)).Select(l => (l.IngredientId, l.Grams)).Should().Equal((ubeId, 400m));
        filling.SubAssemblyId.Should().Be(section.Id);
        filling.CostPerBaseUnit.Should().Be(0.2m);
    }

    [Fact]
    public async Task A_name_another_raw_material_has_or_another_section_takes_is_refused_and_nothing_is_written()
    {
        var (productId, toppingRoleId, toppingId, lines) = await SeedToppingAsync();
        var inHouseId = await MadeCategoryIdAsync();
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2, isProductOwned: true);

        var taken = () => SaveRecipeAsync(productId, new RecipeDraft(
            [Section(toppingRoleId, lines, id: toppingId, made: new MadeIngredientDraft("sugar", inHouseId))], []));
        var twice = () => SaveRecipeAsync(productId, new RecipeDraft(
            [
                Section(toppingRoleId, lines, id: toppingId, made: new MadeIngredientDraft("Butter Icing", inHouseId)),
                Section(fillingRoleId, [lines[1]], made: new MadeIngredientDraft("Butter Icing", inHouseId)),
            ],
            []));

        await taken.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("There's already a raw material named 'sugar'. Give the made ingredient its own name.");
        await twice.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("There's already a raw material named 'Butter Icing'. Give the made ingredient its own name.");
        await ShouldBeAsSeededAsync(toppingId);
    }

    [Fact]
    public async Task Only_a_Made_category_takes_it_and_nothing_is_written_otherwise()
    {
        var (productId, toppingRoleId, toppingId, lines) = await SeedToppingAsync();
        var ingredientsId = await CategoryIdAsync("Ingredients");

        var intoIngredients = () => SaveRecipeAsync(productId, new RecipeDraft(
            [Section(toppingRoleId, lines, id: toppingId, made: new MadeIngredientDraft("Butter Icing", ingredientsId))], []));
        var intoNothing = () => SaveRecipeAsync(productId, new RecipeDraft(
            [Section(toppingRoleId, lines, id: toppingId, made: new MadeIngredientDraft("Butter Icing", ingredientsId + 1000))], []));

        await intoIngredients.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Ingredients is an ingredient category. Put this made ingredient in a Made category.");
        await intoNothing.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The category to put the made ingredient in no longer exists.");
        await ShouldBeAsSeededAsync(toppingId);
    }

    [Fact]
    public async Task Moving_to_In_House_takes_a_section_to_Sub_Assemblies_whatever_its_role()
    {
        var (productId, toppingRoleId, toppingId, lines) = await SeedToppingAsync();
        var inHouseId = await MadeCategoryIdAsync();
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 0);

        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                Section(toppingRoleId, lines, id: toppingId, made: new MadeIngredientDraft("Butter Icing", inHouseId)),
                Section(doughRoleId, [lines[0]], made: new MadeIngredientDraft("House Dough", inHouseId)),
            ],
            []));

        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.Select(s => s.OwnerProductId).ToListAsync(Cancellation)).Should().AllSatisfy(owner => owner.Should().BeNull());
        (await dbContext.RawMaterials.Where(m => m.Source == RawMaterialSource.Made).OrderBy(m => m.Name).Select(m => m.Name).ToListAsync(Cancellation))
            .Should().Equal("Butter Icing", "House Dough");
    }

    [Fact]
    public async Task A_recipe_on_the_Sub_Assemblies_screen_has_a_made_ingredient_made_from_it_and_stays_as_it_is()
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var eggId = await AddIngredientAsync("Egg", costPerGram: 0.09m);
        var milkId = await AddIngredientAsync("Milk", costPerGram: 0.1m);
        var custardId = await SubAssemblies().SaveStandaloneAsync(
            new SubAssembly { Name = "Custard", RoleId = fillingRoleId }, [new RecipeLine(milkId, 500m, IsFormulaBase: true), (eggId, 100m)], Cancellation);
        var inHouseId = await MadeCategoryIdAsync();

        var ingredientId = await SubAssemblies().AddMadeIngredientAsync(custardId, new MadeIngredientDraft(" Custard Filling ", inHouseId), Cancellation);

        await using var dbContext = CreateDbContext();
        var custard = await dbContext.RawMaterials.AsNoTracking().SingleAsync(m => m.Id == ingredientId, Cancellation);
        (custard.Name, custard.Source, custard.WeightLossPercent, custard.CategoryId, custard.SubAssemblyId)
            .Should().Be(("Custard Filling", RawMaterialSource.Made, 0m, inHouseId, (int?)custardId));
        custard.CostPerBaseUnit.Should().Be(0.09833333333333333333m, "₱50 of milk and ₱9 of eggs over 600 g, stored to 20 places");
        (await LinesAsync(custardId)).Select(l => (l.IngredientId, l.Grams, l.IsFormulaBase)).Should().Equal((milkId, 500m, true), (eggId, 100m, false));
    }

    [Fact]
    public async Task A_recipe_a_product_uses_is_left_as_it_is_with_the_products_piece_size_and_add_ins()
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.2m);
        var milkId = await AddIngredientAsync("Milk", costPerGram: 0.1m);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var baseId = await SubAssemblies().SaveStandaloneAsync(
            new SubAssembly { Name = "Ube Base", RoleId = fillingRoleId }, [(ubeId, 300m), (milkId, 100m)], Cancellation);
        var productId = await AddProductAsync("Ube Cheese Bun");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [], [new LinkedUsageDraft(baseId, UsageAuthoredField.WeightPerPiece, 20m) { AddIns = [(cheeseId, 50m)] }]));
        var before = await SubAssemblyTotalsAsync(baseId);

        await SubAssemblies().AddMadeIngredientAsync(baseId, new MadeIngredientDraft("Ube Filling", await MadeCategoryIdAsync()), Cancellation);

        var kept = await SubAssemblyAsync(baseId);
        (kept.Name, kept.OwnerProductId).Should().Be(("Ube Base", (int?)null));
        (await SubAssemblyTotalsAsync(baseId)).Should().Be(before);
        (await LinesAsync(baseId)).Select(l => (l.IngredientId, l.Grams)).Should().Equal((ubeId, 300m), (milkId, 100m));
        await using var dbContext = CreateDbContext();
        var usage = await dbContext.ProductSubAssemblyUsages.SingleAsync(Cancellation);
        (usage.SubAssemblyId, usage.AuthoredValue).Should().Be((baseId, 20m));
        (await dbContext.UsageAddInLines.Select(a => new { a.IngredientId, a.Grams }).SingleAsync(Cancellation)).Should().Be(new { IngredientId = cheeseId, Grams = 50m });
    }

    [Fact]
    public async Task The_Sub_Assemblies_screens_move_refuses_a_set_a_products_own_a_taken_name_and_a_category_that_isnt_Made_and_writes_nothing()
    {
        var (_, _, toppingId, _) = await SeedToppingAsync();
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 3, isPackaging: true);
        var sugarId = await IngredientIdAsync("Sugar");
        var syrupId = await SubAssemblies().SaveStandaloneAsync(new SubAssembly { Name = "Syrup", RoleId = fillingRoleId }, [(sugarId, 100m)], Cancellation);
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        int boxId;
        await using (var seed = CreateDbContext())
        {
            var box = new RawMaterial { Name = "Box", Type = RawMaterialType.Packaging, CategoryId = await AddPackagingCategoryAsync(), BaseUnitId = pieceId };
            seed.RawMaterials.Add(box);
            await seed.SaveChangesAsync(Cancellation);
            boxId = box.Id;
        }

        var setId = await SubAssemblies().SaveStandalonePackagingAsync(new SubAssembly { Name = "Gift Set", RoleId = packagingRoleId }, [(boxId, null, 1m)], Cancellation);
        var inHouseId = await MadeCategoryIdAsync();
        var ingredientsId = await CategoryIdAsync("Ingredients");

        var set = () => SubAssemblies().AddMadeIngredientAsync(setId, new MadeIngredientDraft("Gift", inHouseId), Cancellation);
        var own = () => SubAssemblies().AddMadeIngredientAsync(toppingId, new MadeIngredientDraft("Butter Icing", inHouseId), Cancellation);
        var taken = () => SubAssemblies().AddMadeIngredientAsync(syrupId, new MadeIngredientDraft("box", inHouseId), Cancellation);
        var intoIngredients = () => SubAssemblies().AddMadeIngredientAsync(syrupId, new MadeIngredientDraft("Simple Syrup", ingredientsId), Cancellation);


        await set.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Gift Set is a packaging set. A made ingredient is made from a recipe of ingredients.");
        await own.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cheese Roll Topping is Cheese Roll's own. Move it to In-House from that product's recipe instead.");
        await taken.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("There's already a raw material named 'box'. Give the made ingredient its own name.");
        await intoIngredients.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Ingredients is an ingredient category. Put this made ingredient in a Made category.");
        await ShouldBeAsSeededAsync(toppingId);
        (await LinesAsync(syrupId)).Select(l => l.IngredientId).Should().Equal(sugarId);
    }

    private SubAssemblyRepository SubAssemblies() => new(CreateDbContextFactory());

    private async Task<int> IngredientIdAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterials.Where(m => m.Name == name).Select(m => m.Id).SingleAsync(Cancellation);
    }

    /// <summary>The topping still has its two lines, its product still uses it, and no made ingredient was added.</summary>

    private async Task ShouldBeAsSeededAsync(int toppingId)
    {
        (await LinesAsync(toppingId)).Should().HaveCount(2);

        await using var dbContext = CreateDbContext();
        (await dbContext.ProductSubAssemblyUsages.Select(u => u.SubAssemblyId).ToListAsync(Cancellation)).Should().Equal(toppingId);
        (await dbContext.RawMaterials.AnyAsync(m => m.Source == RawMaterialSource.Made, Cancellation)).Should().BeFalse();
    }
}
