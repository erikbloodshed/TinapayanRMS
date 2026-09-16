using System.Reactive.Linq;
using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Fixtures;
using TinapayanRMS.UI;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.Tests.ViewModels;

/// <summary>
/// The Reports margin chart costs every product from the database, without going through the Products
/// screen's rows. It is the one place that reads a sub-assembly's stored totals as the usage's batch, so
/// it has to add a linked usage's add-ins in itself or it understates every product that has them.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ReportsViewModelMarginTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ReportsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);

        return new ReportsViewModel(
            new RawMaterialPriceHistoryRepository(factory, CreateCostPropagator()),
            new RawMaterialRepository(factory, CreateCostPropagator()),
            new ProductRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            packagingLineRepository,
            new CostingParametersRepository(factory));
    }

    [Fact]
    public async Task The_margin_chart_counts_a_linked_usages_add_ins()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);

        // Cream Loaf owns a 1,000 g / ₱50 dough. Both linkers use 100 g a piece, but only one adds ube.
        var (_, doughId) = await AddProductWithSubAssemblyAsync(
            "Cream Loaf", "Sweet Dough", roleId, [(flourId, 1000m)], weightPerPiece: 100m);
        var plainId = await AddProductAsync("Cream Loaf Plain");
        var ubeLoafId = await AddProductAsync("Cream Loaf Ube");
        await SaveRecipeAsync(plainId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 100m)]));
        await SaveRecipeAsync(ubeLoafId, new RecipeDraft(
            [],
            [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 100m) { AddIns = [(ubeId, 20m)] }]));

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        var plain = viewModel.MarginRows.Should().ContainSingle(r => r.ProductName == "Cream Loaf Plain").Subject;
        var ube = viewModel.MarginRows.Should().ContainSingle(r => r.ProductName == "Cream Loaf Ube").Subject;

        plain.BaseCost.Should().Be(5m, "100 g of a ₱50, 1,000 g batch");
        ube.BaseCost.Should().Be(66m * 100m / 1020m, "100 g of the ₱66, 1,020 g batch the ube makes it");
        ube.BaseCost.Should().BeGreaterThan(plain.BaseCost, "the ube is real money and has to reach the chart");
    }
}
