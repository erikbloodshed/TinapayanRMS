using System.Reactive.Linq;
using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// The Reports summaries, read from the database. The Product Cost Summary costs every product as it's saved, through
/// SavedProductReader, as the Products screen prices the product it opens; before 2026-09-27 Reports had a copy of its
/// own that read a sub-assembly's stored totals as the usage's batch. The add-in figures were worked out for that copy
/// and are unchanged, so the two pricings agree.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ReportsViewModelTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ReportsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);

        return new ReportsViewModel(
            new RawMaterialPriceHistoryRepository(factory),
            new RawMaterialRepository(factory, CreateCostPropagator()),
            new ProductRepository(factory),
            new SavedProductReader(
                new PackagingLinkRepository(factory, packagingLineRepository),
                new ProductSubAssemblyUsageRepository(factory),
                packagingLineRepository,
                new CostingParametersRepository(factory),
                new SubAssemblyLineRepository(factory),
                new ProductRepository(factory)));
    }

    [Fact]
    public async Task The_product_cost_summary_counts_a_linked_usages_add_ins()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);

        // Cream Loaf owns a 1,000 g / ₱50 dough. Both linkers use 100 g a piece, but only one adds ube.
        var (_, doughId) = await AddProductWithSubAssemblyAsync(
            "Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 100m);
        var plainId = await AddProductAsync("Cream Loaf Plain");
        var ubeLoafId = await AddProductAsync("Cream Loaf Ube");
        await SaveRecipeAsync(plainId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 100m)]));
        await SaveRecipeAsync(ubeLoafId, new RecipeDraft(
            [],
            [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 100m) { AddIns = [(ubeId, 20m)] }]));

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        var plain = viewModel.ProductCosts.Should().ContainSingle(r => r.ProductName == "Cream Loaf Plain").Subject;
        var ube = viewModel.ProductCosts.Should().ContainSingle(r => r.ProductName == "Cream Loaf Ube").Subject;

        plain.BaseCost.Should().Be(5m, "100 g of a ₱50, 1,000 g batch");
        ube.BaseCost.Should().Be(66m * 100m / 1020m, "100 g of the ₱66, 1,020 g batch the ube makes it");
        ube.BaseCost.Should().BeGreaterThan(plain.BaseCost, "the ube is real money and has to reach the summary");
    }

    [Fact]
    public async Task The_price_summary_shows_each_purchased_materials_latest_price_and_its_change_per_gram()
    {
        var flourId = await AddIngredientAsync("Flour");
        var kilogramId = await AddKilogramUnitAsync(flourId);
        await AddIngredientAsync("Salt");
        await SetPriceAsync(flourId, null, 0.05m);
        await SetPriceAsync(flourId, kilogramId, 60m);

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        var flour = viewModel.RawMaterialPrices.Should().ContainSingle(r => r.Name == "Flour").Subject;
        flour.UnitName.Should().Be("kilogram");
        flour.Price.Should().Be(60m);
        flour.ChangePercent.Should().Be(20m, "₱60 a kilogram is ₱0.06 a gram, up from ₱0.05");

        var salt = viewModel.RawMaterialPrices.Should().ContainSingle(r => r.Name == "Salt").Subject;
        salt.Price.Should().BeNull("salt has never been priced");
        salt.ChangePercent.Should().BeNull();
    }

    [Fact]
    public async Task A_product_cost_row_asks_for_its_own_product_to_be_opened()
    {
        var pandesalId = await AddProductAsync("Pandesal");
        await AddProductAsync("Ensaymada");

        using var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.ProductCosts.Select(r => r.ProductName).Should().Equal(["Ensaymada", "Pandesal"], "read several at once, they still come by name");
        var row = viewModel.ProductCosts.Should().ContainSingle(r => r.ProductName == "Pandesal").Subject;

        var opened = new List<int>();
        using (viewModel.ProductOpenRequests.Subscribe(opened.Add))
        {
            await viewModel.OpenProductCommand.Execute(row.ProductId);
        }

        opened.Should().Equal(pandesalId);
    }
}
