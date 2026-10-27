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
/// What the Raw Materials list saves at once, without the form's Save: a material dragged or moved into another category
/// (MoveAsync), and a material renamed from its menu (RenameAsync). Each changes that one thing and stamps UpdatedAt.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class RawMaterialMovePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private RawMaterialRepository CreateRepository() => new(CreateDbContextFactory());

    [Fact]
    public async Task MoveAsync_files_several_materials_in_one_save_changing_only_their_category()
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddMaterialAsync("Bread Flour", flourId, costPerGram: 0.05m);
        var cakeFlourId = await AddMaterialAsync("Cake Flour", flourId);
        var before = DateTime.Now;

        await CreateRepository().MoveAsync([new RawMaterialMove(breadFlourId, dryGoodsId), new RawMaterialMove(cakeFlourId, dryGoodsId)], Cancellation);

        await using var dbContext = CreateDbContext();
        var moved = await dbContext.RawMaterials.AsNoTracking().OrderBy(m => m.Name).ToListAsync(Cancellation);
        moved.Should().OnlyContain(m => m.CategoryId == dryGoodsId && m.UpdatedAt >= before);
        moved[0].CostPerBaseUnit.Should().Be(0.05m, "a category costs nothing");
        moved[0].CostUpdatedAt.Should().BeOnOrBefore(before, "so its cost date stays as it was added");
    }

    [Fact]
    public async Task MoveAsync_refuses_a_category_of_the_other_type_and_moves_nothing()
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var breadFlourId = await AddMaterialAsync("Bread Flour", flourId);
        var cakeFlourId = await AddMaterialAsync("Cake Flour", flourId);

        var move = () => CreateRepository().MoveAsync([new RawMaterialMove(breadFlourId, dryGoodsId), new RawMaterialMove(cakeFlourId, boxesId)], Cancellation);

        await move.Should().ThrowAsync<InvalidOperationException>().WithMessage("Boxes is a packaging category.*");
        (await CategoryIdsAsync()).Should().OnlyContain(id => id == flourId);
    }

    [Fact]
    public async Task MoveAsync_files_a_made_ingredient_only_in_a_Made_category()
    {
        var fillingsId = await AddCategoryAsync("Fillings", RawMaterialCategoryType.Ingredient);
        var inHouseId = await AddCategoryAsync("In-House", RawMaterialCategoryType.Made);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var custardId = await AddMaterialAsync("Custard Filling", fillingsId, source: RawMaterialSource.Made);
        var breadFlourId = await AddMaterialAsync("Bread Flour", dryGoodsId);

        var intoIngredients = () => CreateRepository().MoveAsync([new RawMaterialMove(custardId, dryGoodsId)], Cancellation);
        var flourIntoMade = () => CreateRepository().MoveAsync([new RawMaterialMove(breadFlourId, inHouseId)], Cancellation);

        await intoIngredients.Should().ThrowAsync<InvalidOperationException>().WithMessage("Dry Goods is an ingredient category. Put this made ingredient in a Made category.");
        await flourIntoMade.Should().ThrowAsync<InvalidOperationException>().WithMessage("In-House is a Made category*");

        // One saved in an ingredient category before Made ones came in moves out of it into one.
        await CreateRepository().MoveAsync([new RawMaterialMove(custardId, inHouseId)], Cancellation);
        (await CategoryIdsAsync()).Should().BeEquivalentTo([inHouseId, dryGoodsId]);
    }

    [Fact]
    public async Task MoveAsync_refuses_a_category_that_no_longer_exists()
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddMaterialAsync("Bread Flour", flourId);

        var move = () => CreateRepository().MoveAsync([new RawMaterialMove(breadFlourId, flourId + 1000)], Cancellation);

        await move.Should().ThrowAsync<InvalidOperationException>().WithMessage("The category to move it to no longer exists.");
    }

    [Fact]
    public async Task RenameAsync_changes_only_the_name_and_refuses_one_in_use()
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddMaterialAsync("Bread Flour", flourId, costPerGram: 0.05m);
        await AddMaterialAsync("Cake Flour", flourId);
        var before = DateTime.Now;

        await CreateRepository().RenameAsync(breadFlourId, "Strong Flour", Cancellation);

        await using (var dbContext = CreateDbContext())
        {
            var renamed = await dbContext.RawMaterials.AsNoTracking().SingleAsync(m => m.Id == breadFlourId, Cancellation);
            renamed.Name.Should().Be("Strong Flour");
            renamed.UpdatedAt.Should().BeOnOrAfter(before);
            (renamed.CategoryId, renamed.CostPerBaseUnit).Should().Be((flourId, 0.05m));
        }

        var rename = () => CreateRepository().RenameAsync(breadFlourId, "cake flour", Cancellation);
        await rename.Should().ThrowAsync<DbUpdateException>("names are unique, whatever their case");
    }

    private async Task<int> AddCategoryAsync(string name, RawMaterialCategoryType type)
    {
        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = name, Type = type };
        dbContext.RawMaterialCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    /// <summary>Saved past the repository's checks, so a made one can start in an ingredient category, as one saved before Made categories did.</summary>
    private async Task<int> AddMaterialAsync(string name, int categoryId, decimal costPerGram = 0m, RawMaterialSource source = RawMaterialSource.Purchased)
    {
        int? recipeId = source == RawMaterialSource.Made ? await AddEmptyRecipeAsync() : null;
        await using var dbContext = CreateDbContext();
        var material = new RawMaterial
        {
            Name = name,
            Type = RawMaterialType.Ingredient,
            CategoryId = categoryId,
            Source = source,
            WeightLossPercent = source == RawMaterialSource.Made ? 0m : null,
            SubAssemblyId = recipeId,
            CostPerBaseUnit = costPerGram,
            BufferedCostPerBaseUnit = costPerGram,
        };
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
    }

    private async Task<List<int>> CategoryIdsAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterials.Select(m => m.CategoryId).ToListAsync(Cancellation);
    }
}
