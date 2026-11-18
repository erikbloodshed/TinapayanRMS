using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A linked sub-assembly's lines changed in a product's recipe (LinkedUsageDraft.Lines): the product's Save writes them to
/// the sub-assembly itself, on the Sub-Assemblies screen, so every product linking it follows. Without them its lines are
/// left as they are.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class LinkedLinesPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private static LinkedUsageDraft Linked(int subAssemblyId, decimal weightPerPiece, IReadOnlyList<RecipeLine>? lines = null, params (int IngredientId, decimal Grams)[] addIns) =>
        new(subAssemblyId, UsageAuthoredField.WeightPerPiece, weightPerPiece) { Lines = lines, AddIns = addIns };

    /// <summary>Cream Loaf's dough, kept on the Sub-Assemblies screen, which Cream Loaf and Cream Loaf Mini both link.</summary>
    private async Task<(int OwnerId, int MiniId, int DoughId, int FlourId)> SeedSharedDoughAsync()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (ownerId, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        await SaveRecipeAsync(miniId, new RecipeDraft([], [Linked(doughId, 30m)]));
        return (ownerId, miniId, doughId, flourId);
    }

    [Test]
    public async Task New_lines_saved_from_a_linking_product_rewrite_the_sub_assembly_for_every_product_linking_it()
    {
        var (ownerId, miniId, doughId, flourId) = await SeedSharedDoughAsync();
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);

        await SaveRecipeAsync(miniId, new RecipeDraft([], [Linked(doughId, 30m, [new RecipeLine(flourId, 1000m, IsFormulaBase: true), (butterId, 250m)])]));

        await using var dbContext = CreateDbContext();
        var dough = await dbContext.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == doughId, Cancellation);
        (await SubAssemblyTotalsAsync(doughId)).Should().Be((1250m, 150m), "the dough is costed from its new lines");
        dough.OwnerProductId.Should().BeNull("it stays on the Sub-Assemblies screen");
        (await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == doughId).OrderBy(l => l.SortOrder)
                .Select(l => new { l.IngredientId, l.Grams, l.IsFormulaBase }).ToListAsync(Cancellation))
            .Should().Equal(new { IngredientId = flourId, Grams = 1000m, IsFormulaBase = true }, new { IngredientId = butterId, Grams = 250m, IsFormulaBase = false });
        (await dbContext.ProductSubAssemblyUsages.Where(u => u.SubAssemblyId == doughId).Select(u => u.ProductId).OrderBy(id => id).ToListAsync(Cancellation))
            .Should().Equal([ownerId, miniId], "both products still link the one dough");
    }

    [Test]
    public async Task A_linked_usage_without_lines_leaves_the_sub_assemblys_lines_as_they_are()
    {
        var (_, miniId, doughId, flourId) = await SeedSharedDoughAsync();

        await SaveRecipeAsync(miniId, new RecipeDraft([], [Linked(doughId, 45m)]));

        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == doughId).Select(l => new { l.IngredientId, l.Grams }).ToListAsync(Cancellation))
            .Should().Equal(new { IngredientId = flourId, Grams = 1000m });
    }

    [Test]
    public async Task Add_ins_are_checked_against_the_new_lines_not_the_ones_they_replace()
    {
        var (_, miniId, doughId, flourId) = await SeedSharedDoughAsync();
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);

        // Ube as an add-in is fine while the dough has none, and still fine once the new lines take the flour down.
        await SaveRecipeAsync(miniId, new RecipeDraft([], [Linked(doughId, 30m, [(flourId, 800m)], (ubeId, 20m))]));

        // Ube put into the dough's own lines while it's an add-in here is refused, and nothing is written.
        var act = async () => await SaveRecipeAsync(miniId, new RecipeDraft([], [Linked(doughId, 30m, [(flourId, 800m), (ubeId, 50m)], (ubeId, 20m))]));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Ube Flavour*");
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == doughId).Select(l => new { l.IngredientId, l.Grams }).ToListAsync(Cancellation))
            .Should().Equal(new { IngredientId = flourId, Grams = 800m });
    }

    [Test]
    public async Task Invalid_new_lines_are_refused_before_anything_is_written()
    {
        var (_, miniId, doughId, flourId) = await SeedSharedDoughAsync();

        var act = async () => await SaveRecipeAsync(miniId, new RecipeDraft([], [Linked(doughId, 30m, [(flourId, 500m), (flourId, 200m)])]));

        await act.Should().ThrowAsync<BusinessRuleException>();
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == doughId).Select(l => l.Grams).ToListAsync(Cancellation)).Should().Equal(1000m);
        (await dbContext.ProductSubAssemblyUsages.SingleAsync(u => u.ProductId == miniId, Cancellation)).AuthoredValue.Should().Be(30m);
    }
}
