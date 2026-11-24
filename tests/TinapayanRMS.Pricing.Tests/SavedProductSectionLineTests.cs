using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Pricing.Tests;

/// <summary>
/// A saved product whose dough uses part of its sponge on its lines, as its readers (Reports, Print, the Recipe API, the MCP
/// server) price it: the dough costs its grams of sponge, the sponge's pieces come of what's left, and the product counts
/// each gram once.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class SavedProductSectionLineTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task Each_gram_of_the_sponge_is_counted_once()
    {
        await WithoutWastageAsync();
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1, isProductOwned: true);
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Spanish Bread");

        // The dough (1,000 g of flour) takes 300 g of the sponge (400 g of flour), unlocked; each makes one piece of what it has.
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.Yield, 1m)
                {
                    SectionLines = [new SectionLineDraft(RecipeDraft.SectionIdOf(0, 1), 300m, null, 1)],
                },
                new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.Yield, 1m) { IsUnlocked = true },
            ],
            []));
        var product = (await new ProductRepository(CreateDbContextFactory()).GetByIdAsync(productId, Cancellation))!;

        var saved = await CreateReader().ReadAsync(product, Cancellation);

        var (dough, sponge) = (saved.Sections[0], saved.Sections[1]);
        sponge.IsUnlocked.Should().BeTrue();
        (dough.TotalWeight, dough.TotalCostAt(CostBasis.Buffered)).Should().Be((1300m, 65m));
        dough.SectionLines.Should().ContainSingle().Which.SectionName.Should().Be("Sponge");
        dough.CostPerPieceAt(CostBasis.Buffered).Should().Be(65m);
        (sponge.WeightPerPiece, sponge.CostPerPieceAt(CostBasis.Buffered)).Should().Be((100m, 5m), "the 100 g the dough leaves");
        saved.Costs.SubAssemblies.Should().Be(70m, "1,400 g of flour at 0.05, once");
        saved.SectionsWithoutPieceSize.Should().BeEmpty();
    }

    private SavedProductReader CreateReader()
    {
        var factory = CreateDbContextFactory();
        return new SavedProductReader(
            new PackagingLinkRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            new PackagingLineRepository(factory),
            new CostingParametersRepository(factory),
            new SubAssemblyLineRepository(factory),
            new ProductRepository(factory),
            new CostingDefaultsRepository(factory),
            CreateUnitOfWork());
    }
}
