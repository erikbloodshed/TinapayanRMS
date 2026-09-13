using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Fixtures;
using TinapayanRMS.UI;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.Tests.ViewModels;

/// <summary>
/// Coming back to the Products tab re-runs LoadCommand (MainWindow). A cost changed on another tab
/// must reach the selected product's usage rows and price summary, while the selection and the
/// costing fields the user was typing into stay as they were.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductsViewModelRefreshTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task Reloading_updates_usage_costs_changed_on_another_tab_and_keeps_the_selection_and_unsaved_costing()
    {
        var (productId, subAssemblyId) = await SeedProductUsingOneSubAssemblyAsync(totalCost: 24m, totalWeight: 100m, weightPerPiece: 10m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(
            () => viewModel.UsageEdits.Count == 1 && viewModel.EditingUsageRole is not null,
            () => $"usage rows={viewModel.UsageEdits.Count}, role={viewModel.EditingUsageRole?.Name ?? "none"}, roles={viewModel.Roles.Count}, sub-assemblies for role={viewModel.SubAssembliesForSelectedRole.Count}");
        viewModel.UsageEdits.Single().CostPerPiece.Should().Be(2.4m);
        var selected = viewModel.SelectedProduct;
        viewModel.EditingLaborCost = 5m;

        // As another tab would: an ingredient price change has cascaded into the sub-assembly.
        await using (var dbContext = CreateDbContext())
        {
            var subAssembly = await dbContext.SubAssemblies.FindAsync([subAssemblyId], Cancellation);
            subAssembly!.TotalCost = 36m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await viewModel.LoadCommand.Execute();

        viewModel.Products.Should().ContainSingle().Which.Should().BeSameAs(selected);
        viewModel.SelectedProduct.Should().BeSameAs(selected);
        viewModel.SelectedProduct!.Id.Should().Be(productId);
        viewModel.EditingLaborCost.Should().Be(5m, "an unsaved costing field must survive the refresh");
        viewModel.UsageEdits.Should().ContainSingle().Which.CostPerPiece.Should().Be(3.6m);
        viewModel.MaterialCostPerPiece.Should().Be(3.6m);
        var (expectedBaseCost, _, _) = CostingRules.CalculateSellingPrice(3.6m, 5m, 0m, 0m, 0m);
        viewModel.BaseCost.Should().Be(expectedBaseCost);
    }

    private ProductsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var usageRepository = new ProductSubAssemblyUsageRepository(factory);
        var packagingLineRepository = new PackagingLineRepository(factory);
        var costingParametersRepository = new CostingParametersRepository(factory);

        return new ProductsViewModel(
            new ProductRepository(factory),
            usageRepository,
            packagingLineRepository,
            costingParametersRepository,
            new ProductTabLinkRepository(factory, usageRepository, packagingLineRepository),
            new ProductCloner(factory, usageRepository, packagingLineRepository, costingParametersRepository),
            new SubAssemblyRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory, CreateCostPropagator()),
            new ProductValidator(),
            new CostingParametersValidator(),
            new LoggerConfiguration().CreateLogger());
    }

    private async Task<(int ProductId, int SubAssemblyId)> SeedProductUsingOneSubAssemblyAsync(decimal totalCost, decimal totalWeight, decimal weightPerPiece)
    {
        await using var dbContext = CreateDbContext();
        var role = new SubAssemblyRole { Name = "Filling", SortOrder = 1 };
        var subAssembly = new SubAssembly { Name = "Ube filling", Role = role, TotalCost = totalCost, TotalWeight = totalWeight };
        var product = new Product { Name = "Ube pandesal" };
        dbContext.ProductSubAssemblyUsages.Add(new ProductSubAssemblyUsage { Product = product, SubAssembly = subAssembly, WeightPerPiece = weightPerPiece });
        await dbContext.SaveChangesAsync(Cancellation);
        return (product.Id, subAssembly.Id);
    }

    /// <summary>
    /// Selecting a product loads its detail in the background (the ViewModel's selection
    /// subscriptions), with nothing to await. Waits until <paramref name="condition"/> has held
    /// for a moment, so a load still finishing can't change things after the test moves on.
    /// </summary>
    private static async Task WaitUntilSettledAsync(Func<bool> condition, Func<string> describe)
    {
        var deadline = Stopwatch.StartNew();
        var heldFor = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (!condition())
            {
                heldFor.Restart();
            }
            else if (heldFor.Elapsed > TimeSpan.FromMilliseconds(500))
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"The product's detail never finished loading: {describe()}.");
    }
}
