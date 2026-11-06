using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Pricing.Tests;

/// <summary>
/// A saved product's sections are named for its readers (Reports, Print, the Recipe API, the MCP server) as the Products
/// screen names them: two in one role apart by their names, "Dough" beside "Dough: Paste", and one alone by its role.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class SavedProductSectionNameTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task Two_sections_in_one_role_are_told_apart_by_name_and_one_alone_goes_by_its_role()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Chicken Pie");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 35m),
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 500m)], UsageAuthoredField.WeightPerPiece, 15m, SectionName: "Paste"),
                new NewSubAssemblyDraft(fillingRoleId, [(flourId, 300m)], UsageAuthoredField.Yield, 30m),
            ],
            []));
        var product = (await new ProductRepository(CreateDbContextFactory()).GetByIdAsync(productId, Cancellation))!;

        var saved = await CreateReader().ReadAsync(product, Cancellation);

        saved.Sections.Select(s => s.SectionName).Should().Equal("Dough", "Dough: Paste", "Filling");
        saved.Costs.SubAssemblyLines.Select(l => l.Name).Should().Equal("Dough", "Dough: Paste", "Filling");
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
            new CostingDefaultsRepository(factory));
    }
}
