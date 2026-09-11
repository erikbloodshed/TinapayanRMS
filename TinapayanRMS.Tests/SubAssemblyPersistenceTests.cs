using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Tests.Infrastructure;

namespace TinapayanRMS.Tests;

[Collection(DatabaseCollection.Name)]
public class SubAssemblyPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private SubAssemblyRoleRepository CreateRoleRepository() => new(CreateDbContextFactory());

    private SubAssemblyRepository CreateSubAssemblyRepository() => new(CreateDbContextFactory());

    private SubAssemblyLineRepository CreateLineRepository() => new(CreateDbContextFactory());

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

    [Fact]
    public async Task UpdateAsync_rejects_a_role_change_while_a_product_uses_the_sub_assembly()
    {
        var doughRoleId = await SeedRoleAsync("Dough");
        var fillingRoleId = await SeedRoleAsync("Filling", sortOrder: 2);
        var subAssemblyId = await SeedSubAssemblyAsync("Bread Dough", doughRoleId);

        await using (var dbContext = CreateDbContext())
        {
            var product = new Product { Name = "Ensaymada" };
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync(Cancellation);
            dbContext.ProductSubAssemblyUsages.Add(new ProductSubAssemblyUsage
            {
                ProductId = product.Id,
                SubAssemblyId = subAssemblyId,
                WeightPerPiece = 30m,
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var subAssemblyRepository = CreateSubAssemblyRepository();
        var toUpdate = (await subAssemblyRepository.GetByIdAsync(subAssemblyId, Cancellation))!;
        toUpdate.RoleId = fillingRoleId;

        var act = () => subAssemblyRepository.UpdateAsync(toUpdate, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateAsync_allows_a_role_change_when_no_product_uses_the_sub_assembly()
    {
        var doughRoleId = await SeedRoleAsync("Dough");
        var fillingRoleId = await SeedRoleAsync("Filling", sortOrder: 2);
        var subAssemblyId = await SeedSubAssemblyAsync("Bread Dough", doughRoleId);

        var subAssemblyRepository = CreateSubAssemblyRepository();
        var toUpdate = (await subAssemblyRepository.GetByIdAsync(subAssemblyId, Cancellation))!;
        toUpdate.RoleId = fillingRoleId;

        var act = () => subAssemblyRepository.UpdateAsync(toUpdate, Cancellation);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SubAssemblyRepository_UpdateAsync_rejects_a_nonexistent_sub_assembly()
    {
        var roleId = await SeedRoleAsync("Dough");

        var act = () => CreateSubAssemblyRepository().UpdateAsync(new SubAssembly { Id = 999_999, Name = "Ghost", RoleId = roleId }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SubAssemblyRoleRepository_UpdateAsync_rejects_a_nonexistent_role()
    {
        var act = () => CreateRoleRepository().UpdateAsync(new SubAssemblyRole { Id = 999_999, Name = "Ghost", SortOrder = 1 }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetLinesAsync_rejects_a_nonexistent_sub_assembly()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);

        var act = () => CreateLineRepository().SetLinesAsync(999_999, [(sugarId, 100m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetLinesAsync_rejects_a_nonexistent_ingredient_without_deleting_the_existing_lines()
    {
        var roleId = await SeedRoleAsync("Filling");
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        await SetIngredientCostPerGramAsync(sugarId, 0.09m);
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId);
        var lineRepository = CreateLineRepository();
        await lineRepository.SetLinesAsync(subAssemblyId, [(sugarId, 100m)], Cancellation);

        var act = () => lineRepository.SetLinesAsync(subAssemblyId, [(sugarId, 50m), (999_999, 50m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();

        // The existing lines must survive the rejected replacement: the new set is
        // validated before the old rows are deleted, not after.
        var lines = await lineRepository.GetForSubAssemblyAsync(subAssemblyId, Cancellation);
        lines.Should().ContainSingle().Which.Grams.Should().Be(100m);
    }

    [Fact]
    public async Task SubAssemblyRepository_DeleteAsync_removes_an_unused_sub_assembly()
    {
        var roleId = await SeedRoleAsync("Dough");
        var subAssemblyId = await SeedSubAssemblyAsync("Bread Dough", roleId);
        var subAssemblyRepository = CreateSubAssemblyRepository();

        await subAssemblyRepository.DeleteAsync(subAssemblyId, Cancellation);

        (await subAssemblyRepository.GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task SubAssemblyRoleRepository_UpdateAsync_renames_a_role_and_changes_its_sort_order()
    {
        var roleId = await SeedRoleAsync("Dough", sortOrder: 1);
        var roleRepository = CreateRoleRepository();

        await roleRepository.UpdateAsync(new SubAssemblyRole { Id = roleId, Name = "Sourdough", SortOrder = 9 }, Cancellation);

        var updated = (await roleRepository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        updated.Name.Should().Be("Sourdough");
        updated.SortOrder.Should().Be(9);
    }

    [Fact]
    public async Task SubAssemblyRepository_AddAsync_and_GetAllAsync_round_trip_a_sub_assembly()
    {
        var roleId = await SeedRoleAsync("Dough");
        var subAssemblyRepository = CreateSubAssemblyRepository();

        await subAssemblyRepository.AddAsync(new SubAssembly { Name = "Pandesal Dough", RoleId = roleId }, Cancellation);

        var all = await subAssemblyRepository.GetAllAsync(Cancellation);
        var saved = all.Should().ContainSingle().Subject;
        saved.Name.Should().Be("Pandesal Dough");
        saved.Role.Should().NotBeNull();
        saved.Role!.Name.Should().Be("Dough");
    }

    [Fact]
    public async Task SubAssemblyRoleRepository_AddAsync_persists_a_new_role()
    {
        var roleRepository = CreateRoleRepository();

        await roleRepository.AddAsync(new SubAssemblyRole { Name = "Glaze", SortOrder = 5 }, Cancellation);

        var all = await roleRepository.GetAllAsync(Cancellation);
        all.Should().ContainSingle(r => r.Name == "Glaze" && r.SortOrder == 5);
    }
}
