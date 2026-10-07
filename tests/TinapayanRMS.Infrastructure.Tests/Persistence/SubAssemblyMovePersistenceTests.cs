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
/// What the Sub-Assemblies list saves at once, without the form's Save: sub-assemblies dragged or moved into another role
/// (MoveAsync), and one renamed from its menu (RenameAsync). Each changes that one thing, through the checks Save makes.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class SubAssemblyMovePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private SubAssemblyRepository CreateRepository() => new(CreateDbContextFactory());

    private Task<int> SaveStandaloneAsync(string name, int roleId, int ingredientId, decimal grams = 100m) =>
        CreateRepository().SaveStandaloneAsync(new SubAssembly { Name = name, RoleId = roleId }, [(ingredientId, grams)], Cancellation);

    [Fact]
    public async Task MoveAsync_gives_several_sub_assemblies_a_role_in_one_save_changing_nothing_else()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.2m);
        var halayaId = await SaveStandaloneAsync("Ube Halaya", doughRoleId, ubeId);
        var jamId = await SaveStandaloneAsync("Ube Jam", doughRoleId, ubeId, grams: 50m);

        await CreateRepository().MoveAsync([new SubAssemblyMove(halayaId, fillingRoleId), new SubAssemblyMove(jamId, fillingRoleId)], Cancellation);

        await using var dbContext = CreateDbContext();
        var moved = await dbContext.SubAssemblies.AsNoTracking().OrderBy(s => s.Name).ToListAsync(Cancellation);
        moved.Should().OnlyContain(s => s.RoleId == fillingRoleId);
        (moved[0].Name, moved[0].TotalWeight, moved[0].TotalCost).Should().Be(("Ube Halaya", 100m, 20m), "a role costs nothing");
    }

    [Fact]
    public async Task MoveAsync_refuses_a_recipe_into_the_Packaging_role_and_moves_none()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 3, isPackaging: true);
        var ubeId = await AddIngredientAsync("Ube");
        var halayaId = await SaveStandaloneAsync("Ube Halaya", doughRoleId, ubeId);
        var jamId = await SaveStandaloneAsync("Ube Jam", doughRoleId, ubeId);

        var move = () => CreateRepository().MoveAsync([new SubAssemblyMove(halayaId, fillingRoleId), new SubAssemblyMove(jamId, packagingRoleId)], Cancellation);

        await move.Should().ThrowAsync<InvalidOperationException>().WithMessage("The Packaging role is for packaging sets*");
        (await RoleIdsAsync()).Should().OnlyContain(id => id == doughRoleId, "the first refusal moves none");
    }

    [Fact]
    public async Task MoveAsync_checks_each_move_against_the_ones_before_it_where_a_recipe_links_both()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3);
        var sugarId = await AddIngredientAsync("Sugar");
        var glazeId = await SaveStandaloneAsync("Glaze", fillingRoleId, sugarId);
        var ownGlazeId = await SaveStandaloneAsync("Cream Loaf Topping: Glaze", doughRoleId, sugarId);
        var productId = await AddProductAsync("Cream Loaf");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [],
            [
                new LinkedUsageDraft(glazeId, UsageAuthoredField.WeightPerPiece, 5m),
                new LinkedUsageDraft(ownGlazeId, UsageAuthoredField.WeightPerPiece, 5m),
            ]));

        // Either alone is fine. Together, both go by "Glaze" among Cream Loaf's Toppings (SubAssemblyRules.SectionLabel).
        var move = () => CreateRepository().MoveAsync(
            [new SubAssemblyMove(ownGlazeId, toppingRoleId), new SubAssemblyMove(glazeId, toppingRoleId)], Cancellation);

        await move.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cream Loaf, which links this sub-assembly, has two Topping sub-assemblies called Glaze*");
        (await RoleIdsAsync()).Should().BeEquivalentTo([fillingRoleId, doughRoleId], "the first refusal moves none");
    }

    [Fact]
    public async Task MoveAsync_refuses_a_role_that_no_longer_exists()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var ubeId = await AddIngredientAsync("Ube");
        var halayaId = await SaveStandaloneAsync("Ube Halaya", doughRoleId, ubeId);

        var move = () => CreateRepository().MoveAsync([new SubAssemblyMove(halayaId, doughRoleId + 1000)], Cancellation);

        await move.Should().ThrowAsync<InvalidOperationException>().WithMessage("Every sub-assembly needs a role, and that role must still exist.");
    }

    [Fact]
    public async Task RenameAsync_changes_only_the_name_and_refuses_one_in_use()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.2m);
        var halayaId = await SaveStandaloneAsync("Ube Halaya", doughRoleId, ubeId);
        await SaveStandaloneAsync("Ube Jam", doughRoleId, ubeId);

        await CreateRepository().RenameAsync(halayaId, " Ube Spread ", Cancellation);

        await using (var dbContext = CreateDbContext())
        {
            var renamed = await dbContext.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == halayaId, Cancellation);
            (renamed.Name, renamed.RoleId, renamed.TotalCost).Should().Be(("Ube Spread", doughRoleId, 20m));
        }

        var rename = () => CreateRepository().RenameAsync(halayaId, "ube jam", Cancellation);
        await rename.Should().ThrowAsync<InvalidOperationException>("names are unique, whatever their case");
    }

    private async Task<List<int>> RoleIdsAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblies.Select(s => s.RoleId).ToListAsync(Cancellation);
    }
}
