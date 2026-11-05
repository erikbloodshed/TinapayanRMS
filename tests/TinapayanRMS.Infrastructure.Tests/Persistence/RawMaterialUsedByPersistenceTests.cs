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
/// Raw Materials' Used By (IRawMaterialRepository.GetUsedByAsync): every product a material's cost goes into, through a
/// recipe or packaging, directly or through the made and linked ingredients, packaging sets and packaging links between.
/// </summary>
[Collection(DatabaseCollection.B)]
public class RawMaterialUsedByPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private RawMaterialRepository CreateRepository() => new(CreateDbContextFactory());

    private PackagingLineRepository CreatePackagingLineRepository() => new(CreateDbContextFactory());

    private PackagingLinkRepository CreatePackagingLinkRepository() => new(CreateDbContextFactory());

    private async Task<IReadOnlyList<string>> UsedByNamesAsync(int rawMaterialId) =>
        [.. (await CreateRepository().GetUsedByAsync(rawMaterialId, Cancellation)).Select(p => p.Name)];

    /// <summary>A linked ingredient taking a share of <paramref name="linkedToId"/>'s weight and cost.</summary>
    private async Task<int> AddLinkedIngredientAsync(string name, int linkedToId)
    {
        await using var dbContext = CreateDbContext();
        var category = await dbContext.RawMaterialCategories.FirstAsync(c => c.Name == "Ingredients", Cancellation);
        var linked = new RawMaterial
        {
            Name = name,
            Category = category,
            Source = RawMaterialSource.Linked,
            LinkedToId = linkedToId,
            WeightSharePercent = 30m,
            CostSharePercent = 70m,
        };
        dbContext.RawMaterials.Add(linked);
        await dbContext.SaveChangesAsync(Cancellation);
        return linked.Id;
    }

    private async Task<int> AddBoxAsync(string name)
    {
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var categoryId = await AddPackagingCategoryAsync($"{name} Boxes");

        await using var dbContext = CreateDbContext();
        var box = new RawMaterial { Type = RawMaterialType.Packaging, Name = name, CategoryId = categoryId, BaseUnitId = pieceId };
        dbContext.RawMaterials.Add(box);
        await dbContext.SaveChangesAsync(Cancellation);
        return box.Id;
    }

    private Task SetOwnPackagingAsync(int productId, int boxId) =>
        CreatePackagingLineRepository().SetLinesAsync(productId, [(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);

    [Fact]
    public async Task An_ingredient_is_used_by_every_product_whose_sub_assemblies_have_it_each_once_by_name()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [(flourId, 1000m)]);
        var ensaymadaId = await AddProductAsync("Ensaymada");
        await SaveRecipeAsync(ensaymadaId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m), (sugarId, 100m)], UsageAuthoredField.WeightPerPiece, 10m),
                new NewSubAssemblyDraft(fillingRoleId, [(flourId, 50m)], UsageAuthoredField.WeightPerPiece, 5m),
            ],
            []));
        await using (var dbContext = CreateDbContext())
        {
            var breads = new ProductCategory { Name = "Breads" };
            dbContext.ProductCategories.Add(breads);
            await dbContext.SaveChangesAsync(Cancellation);
            await dbContext.Products.Where(p => p.Id == pandesalId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, breads.Id), Cancellation);
        }

        (await CreateRepository().GetUsedByAsync(flourId, Cancellation)).Should().Equal(
            new LinkingProduct(ensaymadaId, "Ensaymada", null),
            new LinkingProduct(pandesalId, "Pandesal", "Breads"));
        (await UsedByNamesAsync(sugarId)).Should().Equal("Ensaymada");
    }

    [Fact]
    public async Task An_unused_material_is_used_by_nothing()
    {
        var flourId = await AddIngredientAsync("Flour");
        var boxId = await AddBoxAsync("Box");
        await AddProductAsync("Pandesal");

        (await UsedByNamesAsync(flourId)).Should().BeEmpty();
        (await UsedByNamesAsync(boxId)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_ingredient_is_used_through_the_made_and_linked_ingredients_it_goes_into_however_deep()
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var eggId = await AddIngredientAsync("Egg");
        var milkId = await AddIngredientAsync("Milk");
        var yolkId = await AddLinkedIngredientAsync("Egg Yolk", eggId);
        var custardBaseId = await AddMadeIngredientAsync("Custard Base", [(yolkId, 100m), (milkId, 400m)]);
        var custardId = await AddMadeIngredientAsync("Custard", [(custardBaseId, 500m)]);
        await AddProductWithSubAssemblyAsync("Custard Cake", fillingRoleId, [(custardId, 300m)]);
        await AddProductWithSubAssemblyAsync("Leche Flan", fillingRoleId, [(eggId, 200m)]);

        (await UsedByNamesAsync(eggId)).Should().Equal("Custard Cake", "Leche Flan");
        (await UsedByNamesAsync(milkId)).Should().Equal("Custard Cake");
        (await UsedByNamesAsync(custardId)).Should().Equal("Custard Cake");
    }

    [Fact]
    public async Task An_ingredient_is_used_through_a_made_ingredients_add_ins()
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var milkId = await AddIngredientAsync("Milk");
        var vanillaId = await AddIngredientAsync("Vanilla");
        var custardId = await AddMadeIngredientAsync("Custard", [(milkId, 400m)]);
        var rawMaterials = CreateRepository();
        await rawMaterials.SaveAsync((await rawMaterials.GetByIdAsync(custardId, Cancellation))!, [], brandPrices: null, [(vanillaId, 5m)], Cancellation);
        await AddProductWithSubAssemblyAsync("Custard Cake", fillingRoleId, [(custardId, 300m)]);

        (await UsedByNamesAsync(vanillaId)).Should().Equal("Custard Cake");
    }

    [Fact]
    public async Task An_add_in_on_a_linked_usage_counts_for_the_product_that_added_it_only()
    {
        var glazeRoleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var ubeId = await AddIngredientAsync("Ube Flavour");
        var (_, glazeId) = await AddProductWithSubAssemblyAsync("Cream Loaf", glazeRoleId, [(sugarId, 100m)]);
        var ubeLoafId = await AddProductAsync("Ube Loaf");
        await SaveRecipeAsync(ubeLoafId, new RecipeDraft(
            [],
            [new LinkedUsageDraft(glazeId, UsageAuthoredField.WeightPerPiece, 5m) { AddIns = [(ubeId, 20m)] }]));

        (await UsedByNamesAsync(ubeId)).Should().Equal("Ube Loaf");
        (await UsedByNamesAsync(sugarId)).Should().Equal("Cream Loaf", "Ube Loaf");
    }

    [Fact]
    public async Task Packaging_is_used_through_own_lines_a_set_and_every_product_linking_them_down_the_chain()
    {
        var boxId = await AddBoxAsync("Box");
        var ribbonId = await AddBoxAsync("Ribbon");
        var setRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var giftSetId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Gift Set", RoleId = setRoleId }, [(ribbonId, null, 1m)], Cancellation);

        var bigId = await AddProductAsync("Big");
        var smallId = await AddProductAsync("Small");
        var miniId = await AddProductAsync("Mini");
        var giftId = await AddProductAsync("Gift");
        var giftMiniId = await AddProductAsync("Gift Mini");
        await SetOwnPackagingAsync(bigId, boxId);
        var links = CreatePackagingLinkRepository();
        await links.SetLinkAsync(smallId, bigId, Cancellation);        // Small -> Big
        await links.SetLinkAsync(miniId, smallId, Cancellation);       // Mini -> Small -> Big
        await links.SetSubAssemblyLinkAsync(giftId, giftSetId, Cancellation);
        await links.SetLinkAsync(giftMiniId, giftId, Cancellation);    // Gift Mini -> Gift -> Gift Set

        (await UsedByNamesAsync(boxId)).Should().Equal("Big", "Mini", "Small");
        (await UsedByNamesAsync(ribbonId)).Should().Equal("Gift", "Gift Mini");
    }

    [Fact]
    public async Task An_ingredient_is_used_by_every_composite_holding_a_product_it_goes_into_but_a_products_packaging_isnt()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");
        var bagId = await AddBoxAsync("Bag");
        var giftBoxId = await AddBoxAsync("Gift Box");
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m)]);
        var (rollId, _) = await AddProductWithSubAssemblyAsync("Cheese Roll", doughRoleId, [(sugarId, 1000m)]);
        await SetOwnPackagingAsync(ensaymadaId, bagId);
        var products = new ProductRepository(CreateDbContextFactory());
        var boxId = await products.SaveAsync(
            new Product { Name = "Pasalubong Box", IsComposite = true },
            new RecipeDraft([], []) { Components = [(ensaymadaId, 6), (rollId, 4)] },
            null, null, null, Cancellation);
        await SetOwnPackagingAsync(boxId, giftBoxId);
        await products.SaveAsync(
            new Product { Name = "Party Package", IsComposite = true },
            new RecipeDraft([], []) { Components = [(boxId, 2), (rollId, 6)] },
            null, null, null, Cancellation);

        (await UsedByNamesAsync(flourId)).Should().Equal("Ensaymada", "Party Package", "Pasalubong Box");
        (await CreateRepository().GetUsedByAsync(flourId, Cancellation)).Select(p => p.IsComposite)
            .Should().Equal([false, true, true], "a composite's tile shows the composite icon");
        (await UsedByNamesAsync(sugarId)).Should().Equal("Cheese Roll", "Party Package", "Pasalubong Box");

        // A product brings its recipe into a composite, not its packaging: the composite's packaging is its own.
        (await UsedByNamesAsync(bagId)).Should().Equal("Ensaymada");
        (await UsedByNamesAsync(giftBoxId)).Should().Equal("Pasalubong Box");
    }

    [Fact]
    public async Task A_products_packaging_is_used_by_every_composite_including_its_products_packaging_however_deep()
    {
        var bagId = await AddBoxAsync("Bag");
        var giftBoxId = await AddBoxAsync("Gift Box");
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var rollId = await AddProductAsync("Cheese Roll");
        await SetOwnPackagingAsync(ensaymadaId, bagId);
        var products = new ProductRepository(CreateDbContextFactory());
        var boxId = await products.SaveAsync(
            new Product { Name = "Pasalubong Box", IsComposite = true, IncludesComponentPackaging = true },
            new RecipeDraft([], []) { Components = [(ensaymadaId, 6), (rollId, 4)] },
            null, null, null, Cancellation);
        await SetOwnPackagingAsync(boxId, giftBoxId);
        await products.SaveAsync(
            new Product { Name = "Party Package", IsComposite = true, IncludesComponentPackaging = true },
            new RecipeDraft([], []) { Components = [(boxId, 2), (rollId, 6)] },
            null, null, null, Cancellation);
        await products.SaveAsync(
            new Product { Name = "Merienda Set", IsComposite = true },
            new RecipeDraft([], []) { Components = [(ensaymadaId, 2), (rollId, 2)] },
            null, null, null, Cancellation);

        // A product linking the box's packaging takes its lines, not what its products bring.
        var giftSetId = await AddProductAsync("Gift Set");
        await CreatePackagingLinkRepository().SetLinkAsync(giftSetId, boxId, Cancellation);

        (await UsedByNamesAsync(bagId)).Should().Equal("Ensaymada", "Party Package", "Pasalubong Box");
        (await UsedByNamesAsync(giftBoxId)).Should().Equal("Gift Set", "Party Package", "Pasalubong Box");
    }

    [Fact]
    public async Task An_ingredient_in_a_composites_own_topping_is_used_by_it_and_by_every_composite_holding_it()
    {
        var toppingRoleId = await AddRoleAsync("Topping", isProductOwned: true);
        var icingId = await AddIngredientAsync("Icing");
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var rollId = await AddProductAsync("Cheese Roll");
        var products = new ProductRepository(CreateDbContextFactory());
        var boxId = await products.SaveAsync(
            new Product { Name = "Pasalubong Box", IsComposite = true },
            new RecipeDraft([new NewSubAssemblyDraft(toppingRoleId, [(icingId, 500m)], UsageAuthoredField.WeightPerPiece, 100m)], [])
            {
                Components = [(ensaymadaId, 6), (rollId, 4)],
            },
            null, null, null, Cancellation);
        await products.SaveAsync(
            new Product { Name = "Party Package", IsComposite = true },
            new RecipeDraft([], []) { Components = [(boxId, 2), (rollId, 6)] },
            null, null, null, Cancellation);

        (await UsedByNamesAsync(icingId)).Should().Equal("Party Package", "Pasalubong Box");
    }

    [Fact]
    public async Task A_products_own_packaging_lines_dont_count_while_its_packaging_links_another()
    {
        var boxId = await AddBoxAsync("Box");
        var ribbonId = await AddBoxAsync("Ribbon");
        var bigId = await AddProductAsync("Big");
        var smallId = await AddProductAsync("Small");
        await SetOwnPackagingAsync(bigId, ribbonId);
        await SetOwnPackagingAsync(smallId, boxId);

        await CreatePackagingLinkRepository().SetLinkAsync(smallId, bigId, Cancellation);

        (await UsedByNamesAsync(boxId)).Should().BeEmpty("Small's packaging reads through to Big's, which has no box");
        (await UsedByNamesAsync(ribbonId)).Should().Equal("Big", "Small");
    }

    [Fact]
    public async Task A_loop_of_ingredients_added_outside_the_app_ends_and_still_finds_the_product()
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var milkId = await AddIngredientAsync("Milk");
        var custardId = await AddMadeIngredientAsync("Custard", [(milkId, 400m)]);
        var custardSkinId = await AddLinkedIngredientAsync("Custard Skin", custardId);
        await AddProductWithSubAssemblyAsync("Custard Cake", fillingRoleId, [(custardSkinId, 300m)]);

        // Custard Skin is cut from Custard, and now Custard is made with Custard Skin: the app refuses that
        // (IngredientDependencies), so it goes in past the repository.
        var recipeId = await RecipeIdOfAsync(custardId);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.SubAssemblyLines.Add(new SubAssemblyLine { SubAssemblyId = recipeId, IngredientId = custardSkinId, Grams = 100m });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        (await UsedByNamesAsync(milkId)).Should().Equal("Custard Cake");
    }

    /// <summary>
    /// Ensaymada, of flour in a bag, is in a Pasalubong Box, which is in a Party Package, which is now in that box too: a
    /// loop of composites, each including the packaging of what it holds. The app refuses one
    /// (CompositeProductRules.EnsureNoCycle), so the last step goes in past the repository.
    /// </summary>
    private async Task<(int FlourId, int BagId)> AddLoopOfCompositesAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var bagId = await AddBoxAsync("Bag");
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m)]);
        await SetOwnPackagingAsync(ensaymadaId, bagId);
        var rollId = await AddProductAsync("Cheese Roll");
        var products = new ProductRepository(CreateDbContextFactory());
        var boxId = await products.SaveAsync(
            new Product { Name = "Pasalubong Box", IsComposite = true, IncludesComponentPackaging = true },
            new RecipeDraft([], []) { Components = [(ensaymadaId, 6), (rollId, 4)] },
            null, null, null, Cancellation);
        var packageId = await products.SaveAsync(
            new Product { Name = "Party Package", IsComposite = true, IncludesComponentPackaging = true },
            new RecipeDraft([], []) { Components = [(boxId, 2), (rollId, 6)] },
            null, null, null, Cancellation);

        await using var dbContext = CreateDbContext();
        dbContext.ProductComponents.Add(new ProductComponent { ProductId = boxId, ComponentProductId = packageId, Quantity = 1 });
        await dbContext.SaveChangesAsync(Cancellation);
        return (flourId, bagId);
    }

    [Fact]
    public async Task A_loop_of_composites_added_outside_the_app_ends_for_what_goes_into_their_recipes()
    {
        var (flourId, _) = await AddLoopOfCompositesAsync();

        (await UsedByNamesAsync(flourId)).Should().Equal("Ensaymada", "Party Package", "Pasalubong Box");
    }

    [Fact]
    public async Task A_loop_of_composites_added_outside_the_app_ends_for_the_packaging_they_include()
    {
        var (_, bagId) = await AddLoopOfCompositesAsync();

        (await UsedByNamesAsync(bagId)).Should().Equal("Ensaymada", "Party Package", "Pasalubong Box");
    }
}
