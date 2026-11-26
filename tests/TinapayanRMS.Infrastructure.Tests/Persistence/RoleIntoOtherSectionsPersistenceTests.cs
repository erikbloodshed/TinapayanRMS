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
/// Settings' Into other sections (SubAssemblyRole.CanGoIntoOtherSections), as it's kept: a recipe's Save refuses a
/// section on another's lines in a role without it; a role keeps it while a product has a section in it on another's lines;
/// and a sub-assembly on one keeps a role with it, moved on the Sub-Assemblies screen.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class RoleIntoOtherSectionsPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    /// <summary>Spanish Bread: a dough with 300 g of a sponge of its own on its lines, the sponge's role ticked or not.</summary>
    private async Task<(int ProductId, RecipeDraft Recipe)> SpongeInDoughAsync(bool spongeGoesIn)
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1, isProductOwned: true);
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true, canGoIntoOtherSections: spongeGoesIn);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Spanish Bread");
        return (productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 60m)
                {
                    SectionLines = [new SectionLineDraft(RecipeDraft.SectionIdOf(0, 1), 300m, null, 1)],
                },
                new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.WeightPerPiece, 0m),
            ],
            []));
    }

    private SubAssemblyRoleRepository CreateRoles() => new(CreateDbContextFactory());

    private async Task<SubAssemblyRole> RoleAsync(string name) =>
        (await CreateRoles().GetAllAsync(Cancellation)).Single(r => r.Name == name);

    [Test]
    public async Task A_section_in_a_role_that_cant_go_into_another_is_refused_on_its_lines_and_nothing_is_written()
    {
        var (productId, recipe) = await SpongeInDoughAsync(spongeGoesIn: false);

        var save = () => SaveRecipeAsync(productId, recipe);

        await save.Should().ThrowAsync<BusinessRuleException>().WithMessage(
            "Sponge can't be on Dough's lines: Settings → Roles doesn't let a Sponge section go into another.*");
        await using var dbContext = CreateDbContext();
        (await dbContext.ProductSubAssemblyUsages.AnyAsync(u => u.ProductId == productId, Cancellation)).Should().BeFalse();
    }

    [Test]
    public async Task The_tick_is_read_and_saved_with_the_role()
    {
        await AddRoleAsync("Sponge", sortOrder: 1);
        var sponge = await RoleAsync("Sponge");
        sponge.CanGoIntoOtherSections.Should().BeFalse();

        sponge.CanGoIntoOtherSections = true;
        await CreateRoles().UpdateAsync(sponge, Cancellation);

        (await RoleAsync("Sponge")).CanGoIntoOtherSections.Should().BeTrue();
    }

    [Test]
    public async Task A_role_with_a_section_on_anothers_lines_cant_be_unticked_and_one_without_can()
    {
        var (productId, recipe) = await SpongeInDoughAsync(spongeGoesIn: true);
        await SaveRecipeAsync(productId, recipe);
        var sponge = await RoleAsync("Sponge");

        sponge.CanGoIntoOtherSections = false;
        var untick = () => CreateRoles().UpdateAsync(sponge, Cancellation);

        await untick.Should().ThrowAsync<BusinessRuleException>().WithMessage(
            "Spanish Bread has a Sponge section on another section's lines. Take it off those lines first, then untick Into other sections.");
        (await RoleAsync("Sponge")).CanGoIntoOtherSections.Should().BeTrue();

        // Off the dough's lines, nothing holds it.
        await SaveRecipeAsync(productId, recipe with { New = [recipe.New[0] with { SectionLines = [] }, recipe.New[1]] });
        await CreateRoles().UpdateAsync(sponge, Cancellation);
        (await RoleAsync("Sponge")).CanGoIntoOtherSections.Should().BeFalse();
    }

    [Test]
    public async Task A_rename_keeps_the_tick()
    {
        await AddRoleAsync("Sponge", sortOrder: 1, canGoIntoOtherSections: true);
        var sponge = await RoleAsync("Sponge");

        sponge.Name = "Starter";
        await CreateRoles().UpdateAsync(sponge, Cancellation);

        (await RoleAsync("Starter")).CanGoIntoOtherSections.Should().BeTrue();
    }

    [Test]
    public async Task A_sub_assembly_on_a_sections_lines_cant_move_to_a_role_that_cant_go_into_another()
    {
        await AddRoleAsync("Base", sortOrder: 1, canGoIntoOtherSections: true);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var starterRoleId = await AddRoleAsync("Starter", sortOrder: 3, canGoIntoOtherSections: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var doughId = await AddRecipeAsync("Sweet Dough", [(flourId, 1000m)]);
        var spongeId = await AddRecipeAsync("Ube Sponge", [(flourId, 200m)]);
        var productId = await AddProductAsync("Ube Bun");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [],
            [
                new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 50m) { SectionLines = [new SectionLineDraft(spongeId, 100m, null, 0)] },
                new LinkedUsageDraft(spongeId, UsageAuthoredField.WeightPerPiece, 0m),
            ]));
        var subAssemblies = new SubAssemblyRepository(CreateDbContextFactory());

        var toFilling = () => subAssemblies.MoveAsync([new SubAssemblyMove(spongeId, fillingRoleId)], Cancellation);

        await toFilling.Should().ThrowAsync<BusinessRuleException>().WithMessage(
            "Ube Bun has Ube Sponge on another section's lines, and Settings → Roles doesn't let a Filling section go into another.*");

        // The dough isn't on another's lines, and a role that can go into one takes the sponge.
        await subAssemblies.MoveAsync([new SubAssemblyMove(doughId, fillingRoleId), new SubAssemblyMove(spongeId, starterRoleId)], Cancellation);
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.Where(s => s.Id == spongeId || s.Id == doughId).OrderBy(s => s.Id).Select(s => s.RoleId).ToListAsync(Cancellation))
            .Should().Equal(fillingRoleId, starterRoleId);
    }
}
