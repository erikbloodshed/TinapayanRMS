using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Pricing.Tests;

/// <summary>
/// A saved product whose sponge goes whole into its dough, as its readers (Reports, Print, the Recipe API, the MCP server)
/// price it: the dough's batch and piece take the sponge in, the sponge costs nothing per piece, and the product counts it once.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class SavedProductFoldTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task The_sponge_is_priced_in_the_doughs_pieces_and_only_there()
    {
        await WithoutWastageAsync();
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1, isProductOwned: true);
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Spanish Bread");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 70m),
                new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.WeightPerPiece, 0m) { GoesInto = RecipeDraft.SectionIdOf(0, 0) },
            ],
            []));
        var product = (await new ProductRepository(CreateDbContextFactory()).GetByIdAsync(productId, Cancellation))!;

        var saved = await CreateReader().ReadAsync(product, Cancellation);

        var (dough, sponge) = (saved.Sections[0], saved.Sections[1]);
        (dough.TotalWeight, dough.TotalCostAt(CostBasis.Buffered)).Should().Be((1400m, 70m));
        dough.FoldedIn.Should().ContainSingle().Which.SectionName.Should().Be("Sponge");
        dough.CostPerPieceAt(CostBasis.Buffered).Should().Be(3.5m);
        sponge.GoesIntoName.Should().Be("Dough");
        sponge.CostPerPieceAt(CostBasis.Buffered).Should().Be(0m);
        saved.Costs.SubAssemblies.Should().Be(3.5m);
        saved.Costs.SubAssemblyLines.Select(l => l.Name).Should().Equal("Dough");
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
