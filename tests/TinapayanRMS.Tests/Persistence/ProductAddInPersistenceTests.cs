using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

/// <summary>
/// Add-ins: a product's own extra ingredients on top of a sub-assembly it links. They hang off the usage,
/// not the sub-assembly, so two products linking the same dough each keep their own and neither moves the
/// dough's stored batch. Written with the rest of the recipe (ProductRepository.SaveAsync).
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductAddInPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductSubAssemblyUsageRepository CreateUsageRepository() => new(CreateDbContextFactory());

    private static LinkedUsageDraft Linked(int subAssemblyId, decimal weightPerPiece, params (int IngredientId, decimal Grams)[] addIns) =>
        new(subAssemblyId, UsageAuthoredField.WeightPerPiece, weightPerPiece) { AddIns = addIns };

    private Task<IReadOnlyList<UsageAddInLine>> AddInsAsync(int productId) =>
        CreateUsageRepository().GetAddInsForProductAsync(productId, Cancellation);

    /// <summary>Cream Loaf owns Sweet Dough; Cream Loaf Mini links it. Returns both products and the dough.</summary>
    private async Task<(int OwnerId, int LinkerId, int DoughId, int FlourId)> SeedLinkedDoughAsync()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (ownerId, doughId) = await AddProductWithSubAssemblyAsync(
            "Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var linkerId = await AddProductAsync("Cream Loaf Mini");
        return (ownerId, linkerId, doughId, flourId);
    }

    [Fact]
    public async Task Saving_a_linked_usage_writes_its_add_ins_against_that_usage()
    {
        var (_, linkerId, doughId, _) = await SeedLinkedDoughAsync();
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);

        await SaveRecipeAsync(linkerId, new RecipeDraft([], [Linked(doughId, 30m, (ubeId, 20m))]));

        var addIns = await AddInsAsync(linkerId);
        var addIn = addIns.Should().ContainSingle().Subject;
        addIn.UsageId.Should().Be(await UsageIdAsync(linkerId, doughId));
        addIn.IngredientId.Should().Be(ubeId);
        addIn.Grams.Should().Be(20m);
        (addIn.Ingredient!.Name, addIn.Ingredient.BufferedCostPerBaseUnit).Should().Be(("Ube Flavour", 0.8m), "an add-in is costed from its ingredient on read");
    }

    [Fact]
    public async Task Saving_the_recipe_again_replaces_the_add_ins_and_drops_the_ones_removed()
    {
        var (_, linkerId, doughId, _) = await SeedLinkedDoughAsync();
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);
        var pandanId = await AddIngredientAsync("Pandan Colour", costPerGram: 0.6m);
        await SaveRecipeAsync(linkerId, new RecipeDraft([], [Linked(doughId, 30m, (ubeId, 20m), (pandanId, 5m))]));

        await SaveRecipeAsync(linkerId, new RecipeDraft([], [Linked(doughId, 30m, (ubeId, 25m))]));

        var addIns = await AddInsAsync(linkerId);
        addIns.Select(l => (l.IngredientId, l.Grams)).Should().Equal((ubeId, 25m));
    }

    [Fact]
    public async Task Two_products_linking_the_same_sub_assembly_each_keep_their_own_add_ins()
    {
        var (_, miniId, doughId, _) = await SeedLinkedDoughAsync();
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);
        var pandanId = await AddIngredientAsync("Pandan Colour", costPerGram: 0.6m);
        var otherId = await AddProductAsync("Cream Loaf Pandan");

        await SaveRecipeAsync(miniId, new RecipeDraft([], [Linked(doughId, 30m, (ubeId, 20m))]));
        await SaveRecipeAsync(otherId, new RecipeDraft([], [Linked(doughId, 45m, (pandanId, 5m))]));

        (await AddInsAsync(miniId)).Select(l => (l.IngredientId, l.Grams)).Should().Equal((ubeId, 20m));
        (await AddInsAsync(otherId)).Select(l => (l.IngredientId, l.Grams)).Should().Equal((pandanId, 5m));
    }

    [Fact]
    public async Task Add_ins_leave_the_linked_sub_assemblys_own_batch_alone()
    {
        var (_, linkerId, doughId, _) = await SeedLinkedDoughAsync();
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);

        await SaveRecipeAsync(linkerId, new RecipeDraft([], [Linked(doughId, 30m, (ubeId, 20m))]));

        await using var dbContext = CreateDbContext();
        var dough = await dbContext.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == doughId, Cancellation);
        dough.TotalWeight.Should().Be(1000m, "an add-in is the linking product's own, not the dough's");
        dough.TotalCost.Should().Be(50m);
        (await dbContext.SubAssemblyLines.CountAsync(l => l.SubAssemblyId == doughId, Cancellation)).Should().Be(1);
    }

    [Fact]
    public async Task An_add_in_already_in_the_linked_recipe_is_refused_before_anything_is_deleted()
    {
        var (_, linkerId, doughId, flourId) = await SeedLinkedDoughAsync();
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);
        await SaveRecipeAsync(linkerId, new RecipeDraft([], [Linked(doughId, 30m, (ubeId, 20m))]));

        var act = async () => await SaveRecipeAsync(linkerId, new RecipeDraft([], [Linked(doughId, 30m, (flourId, 100m))]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Flour*").WithMessage("*Cream Loaf's Dough*");
        (await CreateUsageRepository().GetForProductAsync(linkerId, Cancellation)).Should().ContainSingle("the recipe it refused is left as it was");
        (await AddInsAsync(linkerId)).Select(l => l.IngredientId).Should().Equal(ubeId);
    }

    [Fact]
    public async Task Taking_a_linked_sub_assembly_out_of_the_recipe_takes_its_add_ins_with_it()
    {
        var (_, linkerId, doughId, _) = await SeedLinkedDoughAsync();
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);
        await SaveRecipeAsync(linkerId, new RecipeDraft([], [Linked(doughId, 30m, (ubeId, 20m))]));

        // The usages are cleared with ExecuteDelete, which the change tracker never sees: the cascade is the database's.
        await SaveRecipeAsync(linkerId, new RecipeDraft([], []));

        (await AddInsAsync(linkerId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_the_product_takes_its_usages_add_ins_with_it()
    {
        var (_, linkerId, doughId, _) = await SeedLinkedDoughAsync();
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);
        await SaveRecipeAsync(linkerId, new RecipeDraft([], [Linked(doughId, 30m, (ubeId, 20m))]));

        await new ProductRepository(CreateDbContextFactory()).DeleteAsync(linkerId, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.UsageAddInLines.CountAsync(Cancellation)).Should().Be(0, "Products → usages → add-ins cascades all the way down");
    }

    [Fact]
    public async Task An_ingredient_used_only_as_an_add_in_cant_be_deleted()
    {
        var (_, linkerId, doughId, _) = await SeedLinkedDoughAsync();
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);
        await SaveRecipeAsync(linkerId, new RecipeDraft([], [Linked(doughId, 30m, (ubeId, 20m))]));

        var act = async () => await new RawMaterialRepository(CreateDbContextFactory(), CreateCostPropagator())
            .DeleteAsync(ubeId, Cancellation);

        await act.Should().ThrowAsync<Exception>("the ingredient FK is Restrict, as a sub-assembly line's is");
    }
}
