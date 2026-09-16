using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

/// <summary>
/// The formula's 100% base for baker's percentage: a tick per sub-assembly line, so the same ingredient can
/// be the base of one formula and an extra in another. It rides along with the lines wherever they go —
/// saved with the recipe, read back, and carried into a copy — and never touches cost.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class FormulaBasePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private async Task<IReadOnlyList<SubAssemblyLine>> LinesAsync(int subAssemblyId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblyLines
            .AsNoTracking()
            .Where(l => l.SubAssemblyId == subAssemblyId)
            .OrderBy(l => l.Id)
            .ToListAsync(Cancellation);
    }

    [Fact]
    public async Task A_ticked_line_is_saved_and_read_back()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var waterId = await AddIngredientAsync("Water", costPerGram: 0m);
        var productId = await AddProductAsync("Pandesal");

        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, "Bread Dough", roleId,
                    [new RecipeLine(flourId, 1000m, IsFormulaBase: true), new RecipeLine(waterId, 650m)],
                    UsageAuthoredField.WeightPerPiece, 50m),
            ],
            []));

        var lines = await LinesAsync(await OwnedSubAssemblyIdAsync(productId, "Bread Dough"));
        lines.Single(l => l.IngredientId == flourId).IsFormulaBase.Should().BeTrue();
        lines.Single(l => l.IngredientId == waterId).IsFormulaBase.Should().BeFalse();
    }

    [Fact]
    public async Task Two_flours_can_be_the_base_together()
    {
        var roleId = await AddRoleAsync("Dough");
        var breadFlourId = await AddIngredientAsync("Bread Flour", costPerGram: 0.05m);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", costPerGram: 0.06m);
        var waterId = await AddIngredientAsync("Water", costPerGram: 0m);
        var productId = await AddProductAsync("Milk Bread");

        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, "Sweet Dough", roleId,
                    [
                        new RecipeLine(breadFlourId, 700m, IsFormulaBase: true),
                        new RecipeLine(cakeFlourId, 300m, IsFormulaBase: true),
                        new RecipeLine(waterId, 650m),
                    ],
                    UsageAuthoredField.WeightPerPiece, 50m),
            ],
            []));

        var lines = await LinesAsync(await OwnedSubAssemblyIdAsync(productId, "Sweet Dough"));
        lines.Where(l => l.IsFormulaBase).Sum(l => l.Grams).Should().Be(1000m, "they make 100% between them");
    }

    [Fact]
    public async Task The_base_leaves_the_batch_weight_and_cost_alone()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Pandesal");

        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, "Bread Dough", roleId,
                    [new RecipeLine(flourId, 1000m, IsFormulaBase: true)], UsageAuthoredField.WeightPerPiece, 50m),
            ],
            []));

        var doughId = await OwnedSubAssemblyIdAsync(productId, "Bread Dough");
        await using var dbContext = CreateDbContext();
        var dough = await dbContext.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == doughId, Cancellation);
        dough.TotalWeight.Should().Be(1000m, "a tick is a reading of the batch, not a change to it");
        dough.TotalCost.Should().Be(50m);
    }

    [Fact]
    public async Task A_standalone_sub_assembly_keeps_its_base()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var waterId = await AddIngredientAsync("Water", costPerGram: 0m);

        var id = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "House Dough", RoleId = roleId },
            [new RecipeLine(flourId, 1000m, IsFormulaBase: true), new RecipeLine(waterId, 620m)],
            Cancellation);

        var lines = await LinesAsync(id);
        lines.Single(l => l.IngredientId == flourId).IsFormulaBase.Should().BeTrue();
        lines.Single(l => l.IngredientId == waterId).IsFormulaBase.Should().BeFalse();
    }

    [Fact]
    public async Task A_clone_keeps_the_formulas_base()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var waterId = await AddIngredientAsync("Water", costPerGram: 0m);
        var productId = await AddProductAsync("Pandesal");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, "Bread Dough", roleId,
                    [new RecipeLine(flourId, 1000m, IsFormulaBase: true), new RecipeLine(waterId, 650m)],
                    UsageAuthoredField.WeightPerPiece, 50m),
            ],
            []));

        var cloneId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(productId, "Pandesal Mini", Cancellation);

        var lines = await LinesAsync(await OwnedSubAssemblyIdAsync(cloneId, "Bread Dough"));
        lines.Single(l => l.IngredientId == flourId).IsFormulaBase.Should().BeTrue("the clone's copy is the same formula");
        lines.Single(l => l.IngredientId == waterId).IsFormulaBase.Should().BeFalse();
    }

    [Fact]
    public async Task A_typed_percentage_is_saved_and_read_back()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var waterId = await AddIngredientAsync("Water", costPerGram: 0m);
        var eggsId = await AddIngredientAsync("Eggs", costPerGram: 0.1m);

        var id = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "House Dough", RoleId = roleId },
            [
                new RecipeLine(flourId, 1000m, IsFormulaBase: true),
                new RecipeLine(waterId, 650m, AuthoredBakersPercent: 65m),
                new RecipeLine(eggsId, 100m),
            ],
            Cancellation);

        var lines = await LinesAsync(id);
        lines.Single(l => l.IngredientId == waterId).AuthoredBakersPercent.Should().Be(65m, "its percentage was the figure typed");
        lines.Single(l => l.IngredientId == eggsId).AuthoredBakersPercent.Should().BeNull("its grams were");
        lines.Single(l => l.IngredientId == flourId).AuthoredBakersPercent.Should().BeNull("a base line is always given in grams");
    }

    [Fact]
    public async Task A_base_line_given_as_a_percentage_is_refused()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);

        var act = async () => await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "House Dough", RoleId = roleId },
            [new RecipeLine(flourId, 1000m, IsFormulaBase: true, AuthoredBakersPercent: 100m)],
            Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_clone_keeps_which_figure_was_typed()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var waterId = await AddIngredientAsync("Water", costPerGram: 0m);
        var productId = await AddProductAsync("Pandesal");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, "Bread Dough", roleId,
                    [new RecipeLine(flourId, 1000m, IsFormulaBase: true), new RecipeLine(waterId, 650m, AuthoredBakersPercent: 65m)],
                    UsageAuthoredField.WeightPerPiece, 50m),
            ],
            []));

        var cloneId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(productId, "Pandesal Mini", Cancellation);

        var lines = await LinesAsync(await OwnedSubAssemblyIdAsync(cloneId, "Bread Dough"));
        lines.Single(l => l.IngredientId == waterId).AuthoredBakersPercent.Should().Be(65m, "the copy is the same formula, typed the same way");
    }
}
