using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Tests.Infrastructure;

namespace TinapayanRMS.Tests;

[Collection(DatabaseCollection.Name)]
public class SubAssemblyPersistenceTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => new(fixture.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private TinapayanRMSDbContext CreateDbContext() =>
        new(CreateDbContextOptions());

    private IDbContextFactory<TinapayanRMSDbContext> CreateDbContextFactory() =>
        new TestDbContextFactory(CreateDbContextOptions());

    private SubAssemblyRoleRepository CreateRoleRepository() => new(CreateDbContextFactory());

    private SubAssemblyRepository CreateSubAssemblyRepository() => new(CreateDbContextFactory());

    private SubAssemblyLineRepository CreateLineRepository() => new(CreateDbContextFactory());

    private DbContextOptions<TinapayanRMSDbContext> CreateDbContextOptions() =>
        new DbContextOptionsBuilder<TinapayanRMSDbContext>().UseSqlServer(fixture.ConnectionString).Options;

    private sealed class TestDbContextFactory(DbContextOptions<TinapayanRMSDbContext> options) : IDbContextFactory<TinapayanRMSDbContext>
    {
        public TinapayanRMSDbContext CreateDbContext() => new(options);
    }

    private async Task<int> SeedRoleAsync(string name, int sortOrder = 1)
    {
        await using var dbContext = CreateDbContext();
        var role = new SubAssemblyRole { Name = name, SortOrder = sortOrder };
        dbContext.SubAssemblyRoles.Add(role);
        await dbContext.SaveChangesAsync(Cancellation);
        return role.Id;
    }

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
        await using var dbContext = CreateDbContext();
        var ingredient = new Ingredient
        {
            Name = name,
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Purchased,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitQty = 1000m,
        };
        dbContext.Ingredients.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return ingredient.Id;
    }

    private async Task SetIngredientCostPerGramAsync(int ingredientId, decimal costPerGram)
    {
        await using var dbContext = CreateDbContext();
        var ingredient = await dbContext.Ingredients.SingleAsync(i => i.Id == ingredientId, Cancellation);
        ingredient.CostPerGram = costPerGram;
        await dbContext.SaveChangesAsync(Cancellation);
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
    public async Task SetLinesAsync_computes_TotalWeight_and_TotalCost()
    {
        var roleId = await SeedRoleAsync("Dough");
        var categoryId = await SeedCategoryAsync("Flours");
        var flourId = await SeedPurchasedIngredientAsync("Flour", categoryId);
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        await SetIngredientCostPerGramAsync(flourId, 0.05m);
        await SetIngredientCostPerGramAsync(sugarId, 0.09m);
        var subAssemblyId = await SeedSubAssemblyAsync("Bread Dough", roleId);

        await CreateLineRepository().SetLinesAsync(subAssemblyId,
        [
            (flourId, 800m),
            (sugarId, 200m),
        ], Cancellation);

        var subAssembly = await GetSubAssemblyAsync(subAssemblyId);
        subAssembly.TotalWeight.Should().Be(1000m);
        subAssembly.TotalCost.Should().Be(58m); // 800*0.05 + 200*0.09
    }

    [Fact]
    public async Task GetForSubAssemblyAsync_returns_lines_with_the_ingredient_populated()
    {
        var roleId = await SeedRoleAsync("Filling");
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId);

        await CreateLineRepository().SetLinesAsync(subAssemblyId, [(sugarId, 300m)], Cancellation);

        var lines = await CreateLineRepository().GetForSubAssemblyAsync(subAssemblyId, Cancellation);
        var line = lines.Should().ContainSingle().Subject;
        line.Grams.Should().Be(300m);
        line.Ingredient.Should().NotBeNull();
        line.Ingredient!.Name.Should().Be("Sugar");
    }

    [Fact]
    public async Task SetLinesAsync_rejects_duplicate_ingredients()
    {
        var roleId = await SeedRoleAsync("Filling");
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId);

        var act = () => CreateLineRepository().SetLinesAsync(subAssemblyId,
        [
            (sugarId, 100m),
            (sugarId, 50m),
        ], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Replacing_lines_overwrites_the_previous_set_and_recomputes_totals()
    {
        var roleId = await SeedRoleAsync("Filling");
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var waterId = await SeedPurchasedIngredientAsync("Water", categoryId);
        await SetIngredientCostPerGramAsync(sugarId, 0.09m);
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId);

        var lineRepository = CreateLineRepository();
        await lineRepository.SetLinesAsync(subAssemblyId, [(sugarId, 100m), (waterId, 100m)], Cancellation);
        await lineRepository.SetLinesAsync(subAssemblyId, [(sugarId, 500m)], Cancellation);

        var lines = await lineRepository.GetForSubAssemblyAsync(subAssemblyId, Cancellation);
        lines.Should().ContainSingle();

        var subAssembly = await GetSubAssemblyAsync(subAssemblyId);
        subAssembly.TotalWeight.Should().Be(500m);
        subAssembly.TotalCost.Should().Be(45m);
    }

    [Fact]
    public async Task Deleting_an_ingredient_that_is_in_use_by_a_sub_assembly_is_blocked()
    {
        var roleId = await SeedRoleAsync("Filling");
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId);
        await CreateLineRepository().SetLinesAsync(subAssemblyId, [(sugarId, 100m)], Cancellation);

        await using var dbContext = CreateDbContext();
        var act = () => dbContext.Ingredients.Where(i => i.Id == sugarId).ExecuteDeleteAsync(Cancellation);

        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task Deleting_a_sub_assembly_cascades_to_its_own_lines()
    {
        var roleId = await SeedRoleAsync("Filling");
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId);
        await CreateLineRepository().SetLinesAsync(subAssemblyId, [(sugarId, 100m)], Cancellation);

        await using (var dbContext = CreateDbContext())
        {
            await dbContext.SubAssemblies.Where(s => s.Id == subAssemblyId).ExecuteDeleteAsync(Cancellation);
        }

        (await CreateLineRepository().GetForSubAssemblyAsync(subAssemblyId, Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_role_that_is_in_use_by_a_sub_assembly_is_blocked()
    {
        var roleId = await SeedRoleAsync("Dough");
        await SeedSubAssemblyAsync("Bread Dough", roleId);

        var act = () => CreateRoleRepository().DeleteAsync(roleId, Cancellation);

        // ExecuteDeleteAsync issues a raw DELETE outside EF Core's change tracker, so an
        // FK violation surfaces as the driver's own SqlException, not a DbUpdateException.
        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task Deleting_an_unused_role_succeeds()
    {
        var roleId = await SeedRoleAsync("Dough");

        await CreateRoleRepository().DeleteAsync(roleId, Cancellation);

        (await CreateRoleRepository().GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_on_a_sub_assembly_preserves_TotalWeight_and_TotalCost()
    {
        var roleId = await SeedRoleAsync("Dough");
        var categoryId = await SeedCategoryAsync("Flours");
        var flourId = await SeedPurchasedIngredientAsync("Flour", categoryId);
        await SetIngredientCostPerGramAsync(flourId, 0.05m);
        var subAssemblyId = await SeedSubAssemblyAsync("Bread Dough", roleId);
        await CreateLineRepository().SetLinesAsync(subAssemblyId, [(flourId, 1000m)], Cancellation);

        var subAssemblyRepository = CreateSubAssemblyRepository();
        var toUpdate = (await subAssemblyRepository.GetByIdAsync(subAssemblyId, Cancellation))!;
        toUpdate.Name = "Renamed Dough";
        await subAssemblyRepository.UpdateAsync(toUpdate, Cancellation);

        var updated = await GetSubAssemblyAsync(subAssemblyId);
        updated.Name.Should().Be("Renamed Dough");
        updated.TotalWeight.Should().Be(1000m);
        updated.TotalCost.Should().Be(50m);
    }
}
