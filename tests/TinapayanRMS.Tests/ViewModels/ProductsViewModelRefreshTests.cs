using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
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
/// must reach the selected product's recipe rows and cost summary, while the selection and the
/// costing fields the user was typing into stay as they were. Save writes the whole form at once.
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
            () => viewModel.UsageEdits.Count == 1,
            () => $"usage rows={viewModel.UsageEdits.Count}, roles={viewModel.Roles.Count}");
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
        viewModel.Costs.Material.Should().Be(3.6m);
        var (expectedBaseCost, _, _) = CostingRules.CalculateSellingPrice(3.6m, 5m, 0m, 0m, 0m, 0m);
        viewModel.Costs.BaseCost.Should().Be(expectedBaseCost);
        viewModel.CostShares.Should().ContainSingle().Which.Amount.Should().Be(3.6m);
    }

    [Fact]
    public async Task One_Save_writes_a_new_products_description_recipe_and_costing_together()
    {
        await using (var dbContext = CreateDbContext())
        {
            dbContext.SubAssemblies.Add(new SubAssembly
            {
                Name = "Ube filling",
                Role = new SubAssemblyRole { Name = "Filling", SortOrder = 1 },
                TotalCost = 24m,
                TotalWeight = 100m,
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Ube pandesal";
        viewModel.EditingDescription = "Soft bread with ube filling";
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageSubAssembly = viewModel.UsageFormSubAssemblies.Single();
        viewModel.EditingUsageWeightPerPiece = 10m;
        await viewModel.ApplyUsageCommand.Execute();
        viewModel.EditingLaborCost = 2m;
        viewModel.EditingAdministrativeCost = 0.5m;

        viewModel.UsageEdits.Should().ContainSingle().Which.CostPerPiece.Should().Be(2.4m);
        viewModel.Costs.BaseCost.Should().Be(4.9m, "₱2.40 of filling, ₱2 labor and ₱0.50 administrative");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        int productId;
        await using (var dbContext = CreateDbContext())
        {
            var product = await dbContext.Products.SingleAsync(Cancellation);
            productId = product.Id;
            product.Name.Should().Be("Ube pandesal");
            product.Description.Should().Be("Soft bread with ube filling");
            (await dbContext.ProductSubAssemblyUsages.SingleAsync(Cancellation)).WeightPerPiece.Should().Be(10m);
            var costing = await dbContext.CostingParameters.SingleAsync(Cancellation);
            costing.LaborCost.Should().Be(2m);
            costing.AdministrativeCost.Should().Be(0.5m);
        }

        // The form now shows the saved product, which can be linked and cloned.
        await WaitUntilSettledAsync(
            () => viewModel.SelectedProduct?.Id == productId && viewModel.UsageEdits.Count == 1,
            () => $"selected={viewModel.SelectedProduct?.Id}, usage rows={viewModel.UsageEdits.Count}");
        viewModel.HeaderTitle.Should().Be("Ube pandesal");
        viewModel.ShowLinks.Should().BeTrue();
        viewModel.LastUpdatedText.Should().NotBe("—");
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
            new SubAssemblyLineRepository(factory),
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
