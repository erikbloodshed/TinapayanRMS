using AwesomeAssertions;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.QuickFind;
using TinapayanRMS.UseCases.Tests.Fixtures;

namespace TinapayanRMS.UseCases.Tests.QuickFind;

/// <summary>What Find reads: every raw material, standalone sub-assembly and product, and not a sub-assembly a product owns.</summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class QuickFindQueriesTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task Every_record_Find_can_open_is_read_and_a_products_own_section_is_not()
    {
        var flourId = await AddIngredientAsync("Bread Flour");
        var spongeId = await AddRecipeAsync("Sponge", [(flourId, 500m)]);
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var (productId, ownedId) = await AddProductWithSubAssemblyAsync("Cheese Roll", fillingRoleId, [(flourId, 100m)]);

        var found = await new QuickFindQueries(
            new RawMaterialRepository(CreateDbContextFactory()),
            new SubAssemblyRepository(CreateDbContextFactory()),
            new ProductRepository(CreateDbContextFactory())).ReadAsync(Cancellation);

        found.RawMaterials.Select(m => m.Id).Should().Contain(flourId);
        found.SubAssemblies.Select(s => s.Id).Should().Contain(spongeId).And.NotContain(ownedId);
        found.Products.Select(p => p.Id).Should().Equal(productId);
    }
}
