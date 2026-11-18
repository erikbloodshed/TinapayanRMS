using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class IngredientPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private RawMaterialRepository CreateRepository() => new(CreateDbContextFactory());

    private async Task<int> SeedCategoryAsync()
    {
        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = "Flour" };
        dbContext.RawMaterialCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    [Test]
    public async Task A_purchased_ingredient_can_be_added_and_read_back()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterial
        {
            Name = "All-Purpose Flour",
            CategoryId = categoryId,
            Source = RawMaterialSource.Purchased,
        }, Cancellation);

        var ingredients = await repository.GetAllAsync(Cancellation);
        var ingredient = ingredients.Should().ContainSingle().Subject;
        ingredient.Name.Should().Be("All-Purpose Flour");
        ingredient.Source.Should().Be(RawMaterialSource.Purchased);
        ingredient.Category.Should().NotBeNull();
        ingredient.Units.Should().BeEmpty();
    }

    [Test]
    public async Task A_made_ingredient_can_be_added_with_a_weight_loss_percent()
    {
        var categoryId = await SeedCategoryAsync();
        var madeCategoryId = await MadeCategoryIdAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterial
        {
            Name = "Simple Syrup",
            CategoryId = madeCategoryId,
            Source = RawMaterialSource.Made,
            SubAssemblyId = await AddEmptyRecipeAsync(),
            WeightLossPercent = 20m,
        }, Cancellation);

        var ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        ingredient.Source.Should().Be(RawMaterialSource.Made);
        ingredient.WeightLossPercent.Should().Be(20m);
    }

    [Test]
    public async Task AddAsync_rejects_a_made_ingredient_without_a_weight_loss_percent()
    {
        var categoryId = await SeedCategoryAsync();
        var madeCategoryId = await MadeCategoryIdAsync();

        var act = async () => await CreateRepository().AddAsync(new RawMaterial
        {
            Name = "Invalid",
            CategoryId = madeCategoryId,
            Source = RawMaterialSource.Made,
            SubAssemblyId = await AddEmptyRecipeAsync(),
        }, Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
    public async Task UpdateAsync_rejects_changing_an_ingredients_source()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterial
        {
            Name = "Sugar",
            CategoryId = categoryId,
            Source = RawMaterialSource.Purchased,
        }, Cancellation);

        var ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        ingredient.Source = RawMaterialSource.Made;
        ingredient.SubAssemblyId = await AddEmptyRecipeAsync();
        ingredient.WeightLossPercent = 20m;

        var act = () => repository.UpdateAsync(ingredient, Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
    public async Task GetAllAsync_and_GetByIdAsync_include_the_units_largest_first_with_their_unit_of_measure()
    {
        var categoryId = await SeedCategoryAsync();
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await using (var dbContext = CreateDbContext())
        {
            dbContext.RawMaterials.Add(new RawMaterial
            {
                Name = "Flour",
                CategoryId = categoryId,
                Source = RawMaterialSource.Purchased,
                Units =
                [
                    new RawMaterialUnit { UnitOfMeasureId = packId, BaseUnitsPerUnit = 1000m, Description = "Small pack (1 kg)" },
                    new RawMaterialUnit { UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m },
                ],
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var repository = CreateRepository();
        var fromAll = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        var byId = await repository.GetByIdAsync(fromAll.Id, Cancellation);

        foreach (var ingredient in new[] { fromAll, byId! })
        {
            ingredient.Category.Should().NotBeNull();
            ingredient.Units.Select(u => u.UnitOfMeasure!.Name).Should().Equal("bag", "pack");
            ingredient.Units[1].Description.Should().Be("Small pack (1 kg)");
        }
    }

    [Test]
    public async Task GetByIdAsync_returns_null_for_a_nonexistent_ingredient()
    {
        (await CreateRepository().GetByIdAsync(999_999, Cancellation)).Should().BeNull();
    }

    [Test]
    public async Task UpdateAsync_renames_a_purchased_ingredient_and_preserves_its_CostPerGram()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterial
        {
            Name = "Sugar",
            CategoryId = categoryId,
            Source = RawMaterialSource.Purchased,
        }, Cancellation);
        var ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        var kilogramUnitId = await AddKilogramUnitAsync(ingredient.Id);
        await SetPriceAsync(ingredient.Id, kilogramUnitId, 90m);

        ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        ingredient.Name = "Refined Sugar";
        ingredient.CostPerBaseUnit = 999m; // the form never owns this
        await repository.UpdateAsync(ingredient, Cancellation);

        var updated = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        updated.Name.Should().Be("Refined Sugar");
        // A blind dbContext.Set<T>().Update(incoming) regression would overwrite this server-owned
        // field on every plain rename (PLAN.md's documented Phase 6 bug).
        updated.CostPerBaseUnit.Should().Be(0.09m);
        updated.Units.Should().ContainSingle("a rename leaves the units alone");
    }

    [Test]
    public async Task UpdateAsync_rejects_a_nonexistent_ingredient()
    {
        var categoryId = await SeedCategoryAsync();
        var madeCategoryId = await MadeCategoryIdAsync();

        var act = async () => await CreateRepository().UpdateAsync(new RawMaterial
        {
            Id = 999_999,
            Name = "Ghost",
            CategoryId = madeCategoryId,
            Source = RawMaterialSource.Made,
            SubAssemblyId = await AddEmptyRecipeAsync(),
            WeightLossPercent = 10m,
        }, Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
    public async Task DeleteAsync_removes_the_ingredient()
    {
        var categoryId = await SeedCategoryAsync();
        var madeCategoryId = await MadeCategoryIdAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterial
        {
            Name = "Water",
            CategoryId = madeCategoryId,
            Source = RawMaterialSource.Made,
            SubAssemblyId = await AddEmptyRecipeAsync(),
            WeightLossPercent = 0m,
        }, Cancellation);

        var ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        await repository.DeleteAsync(ingredient.Id, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    [Test]
    public async Task DeleteAsync_removes_an_ingredient_along_with_its_units_and_the_prices_recorded_in_them()
    {
        // Price history references the ingredient (Cascade) and one of its units (Restrict), and
        // the units cascade from the ingredient too: the database must accept removing all three.
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterial { Name = "Flour", CategoryId = categoryId, Source = RawMaterialSource.Purchased }, Cancellation);
        var ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        var kilogramUnitId = await AddKilogramUnitAsync(ingredient.Id);
        await SetPriceAsync(ingredient.Id, kilogramUnitId, 50m);

        await repository.DeleteAsync(ingredient.Id, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.RawMaterialUnits.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.RawMaterialPriceHistories.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Test]
    public async Task AddAsync_rejects_an_ingredient_with_a_base_unit_since_it_is_always_the_gram()
    {
        var categoryId = await SeedCategoryAsync();
        var kilogramId = await SeedUnitOfMeasureAsync("kilogram", "kg");

        var act = async () => await CreateRepository().AddAsync(new RawMaterial
        {
            Type = RawMaterialType.Ingredient,
            Name = "Sugar",
            CategoryId = categoryId,
            BaseUnitId = kilogramId,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*gram*");
    }

    [Test]
    public async Task UpdateAsync_rejects_turning_an_ingredient_into_packaging()
    {
        var categoryId = await SeedCategoryAsync();
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterial { Name = "Sugar", CategoryId = categoryId, Source = RawMaterialSource.Purchased }, Cancellation);
        var sugar = (await repository.GetAllAsync(Cancellation)).Single();

        sugar.Type = RawMaterialType.Packaging;
        sugar.BaseUnitId = pieceId;
        var act = () => repository.UpdateAsync(sugar, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*type*");
    }

    [Test]
    public async Task An_ingredient_and_a_packaging_material_cannot_share_a_name()
    {
        var categoryId = await SeedCategoryAsync();
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterial { Name = "Butter", CategoryId = categoryId }, Cancellation);
        var packagingCategoryId = await AddPackagingCategoryAsync();

        var act = async () => await repository.AddAsync(new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = "Butter",
            CategoryId = packagingCategoryId,
            BaseUnitId = pieceId,
        }, Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task The_database_rejects_an_ingredient_with_a_base_unit_even_past_the_repository()
    {
        // A check constraint backs RawMaterialRules.EnsureFieldsMatchType up.
        var categoryId = await SeedCategoryAsync();
        var kilogramId = await SeedUnitOfMeasureAsync("kilogram", "kg");
        await using var dbContext = CreateDbContext();
        dbContext.RawMaterials.Add(new RawMaterial { Type = RawMaterialType.Ingredient, Name = "Sugar", CategoryId = categoryId, BaseUnitId = kilogramId });

        var act = () => dbContext.SaveChangesAsync(Cancellation);

        (await act.Should().ThrowAsync<DbUpdateException>()).WithInnerException<Npgsql.PostgresException>()
            .WithMessage("*CK_RawMaterials_BaseUnitMatchesType*");
    }

    [Test]
    public async Task Duplicate_ingredient_names_are_rejected()
    {
        var categoryId = await SeedCategoryAsync();
        var madeCategoryId = await MadeCategoryIdAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterial
        {
            Name = "Butter",
            CategoryId = madeCategoryId,
            Source = RawMaterialSource.Made,
            SubAssemblyId = await AddEmptyRecipeAsync(),
            WeightLossPercent = 0m,
        }, Cancellation);

        var act = async () => await repository.AddAsync(new RawMaterial
        {
            Name = "Butter",
            CategoryId = madeCategoryId,
            Source = RawMaterialSource.Made,
            SubAssemblyId = await AddEmptyRecipeAsync(),
            WeightLossPercent = 0m,
        }, Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
