using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// The Products screen's Scaling tab, on real repositories: the recipe the form holds scaled to a number of sellable units,
/// worked out while the tab shows and again as the form changes, made ingredients opening into their saved BOMs and a
/// composite's products into their saved recipes. The number isn't part of the product.
/// </summary>
[Collection(DatabaseCollection.C)]
public class ProductsViewModelScalingTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public Task The_tab_scales_the_recipe_in_whole_batches_and_opens_a_made_ingredient_into_its_bom() => StaThread.RunAsync(async () =>
    {
        var productId = await AddEnsaymadaAsync();
        var viewModel = await OpenAsync(productId);
        viewModel.Scaling.Rows.Should().BeEmpty("the tab is only worked out while it shows");

        viewModel.Scaling.Units = 150;
        viewModel.SelectedDetailsTab = ProductsViewModel.ScalingTab;

        // 150 pieces of a 100-piece batch: 2 batches, 1,800 g of flour and 200 g of syrup, which is 200 g of sugar.
        var dough = viewModel.Scaling.Rows.Should().ContainSingle().Subject;
        (dough.Name, dough.Detail, dough.AmountText).Should().Be(("Dough", "2 batches · 200 pcs (50 extra)", "2,000 g"));
        dough.Children.Select(r => (r.Name, r.AmountText)).Should().Equal(("Flour", "1,800 g"), ("Syrup", "200 g"));
        dough.Children[1].Children.Select(r => (r.Name, r.AmountText, r.Cost)).Should().Equal(("Sugar", "200 g", 18m));
        viewModel.Scaling.TotalCost.Should().Be(108m, "1,800 g of flour at 0.05 and 200 g of syrup at 0.09");
        viewModel.Scaling.PiecesText.Should().BeEmpty("a unit is one piece");
        viewModel.History.IsDirty.Should().BeFalse("the number to scale to isn't an edit");
    });

    [Fact]
    public Task A_change_to_the_quantity_required_rescales_the_tab() => StaThread.RunAsync(async () =>
    {
        var productId = await AddEnsaymadaAsync();
        var viewModel = await OpenAsync(productId);
        viewModel.Scaling.Units = 20;
        viewModel.SelectedDetailsTab = ProductsViewModel.ScalingTab;
        viewModel.Scaling.Rows.Single().Detail.Should().Be("1 batch · 100 pcs (80 extra)");

        viewModel.EditingQuantityRequired = 8;

        viewModel.Scaling.Rows.Single().Detail.Should().Be("2 batches · 200 pcs (40 extra)", "20 boxes of 8 are 160 pieces");
        viewModel.Scaling.PiecesText.Should().Be("= 160 pieces");
    });

    [Fact]
    public Task A_composites_products_open_into_their_saved_recipes() => StaThread.RunAsync(async () =>
    {
        var ensaymadaId = await AddEnsaymadaAsync();
        var flourId = await IngredientIdAsync("Flour");
        var doughRoleId = await RoleIdAsync("Dough");
        var (cheeseRollId, _) = await AddProductWithSubAssemblyAsync("Cheese Roll", doughRoleId, [(flourId, 1000m)], weightPerPiece: 100m);
        var boxId = await new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = "Pasalubong Box", IsComposite = true },
            new RecipeDraft([], []) { Components = [(ensaymadaId, 6), (cheeseRollId, 4)] },
            null,
            null,
            null,
            Cancellation);
        var viewModel = await OpenAsync(boxId, vm => vm.ComponentsTable.ComponentEdits.Count == 2 && vm.ComponentsTable.ComponentEdits.All(r => r.RecipeCost is not null));

        viewModel.Scaling.Units = 5;
        viewModel.SelectedDetailsTab = ProductsViewModel.ScalingTab;
        await WaitUntilSettledAsync(
            () => viewModel.Scaling.Rows.Count == 2 && viewModel.Scaling.Rows.All(r => r.Detail != RecipeScaler.ReadingText),
            () => string.Join(", ", viewModel.Scaling.Rows.Select(r => $"{r.Name}: {r.Detail}")));

        viewModel.Scaling.Rows.Select(r => (r.Name, r.Detail)).Should().Equal(("Ensaymada × 6", "30 pcs"), ("Cheese Roll × 4", "20 pcs"));
        viewModel.Scaling.Rows[0].Children.Single().Detail.Should().Be("1 batch · 100 pcs (70 extra)");
        viewModel.Scaling.Rows[1].Children.Single().Detail.Should().Be("2 batches · 20 pcs", "10 of 100 g from a 1,000 g batch");
    });

    [Fact]
    public Task The_number_to_scale_to_stays_for_the_next_product_picked() => StaThread.RunAsync(async () =>
    {
        var productId = await AddEnsaymadaAsync();
        var flourId = await IngredientIdAsync("Flour");
        var (cheeseRollId, _) = await AddProductWithSubAssemblyAsync("Cheese Roll", await RoleIdAsync("Dough"), [(flourId, 1000m)], weightPerPiece: 100m);
        var viewModel = await OpenAsync(productId);
        viewModel.SelectedDetailsTab = ProductsViewModel.ScalingTab;
        viewModel.Scaling.Units = 25;

        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == cheeseRollId);
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.SelectedProduct?.Id == cheeseRollId && viewModel.UsageForm.FormLines.Count == 1,
            () => $"selected={viewModel.SelectedProduct?.Id}, lines={viewModel.UsageForm.FormLines.Count}");

        viewModel.Scaling.Units.Should().Be(25);
        viewModel.Scaling.Rows.Single().Detail.Should().Be("3 batches · 30 pcs (5 extra)");
    });

    [Fact]
    public Task An_amount_of_one_ingredient_makes_the_most_units_whose_whole_batches_it_covers() => StaThread.RunAsync(async () =>
    {
        var productId = await AddEnsaymadaAsync();
        var viewModel = await OpenAsync(productId);
        viewModel.SelectedDetailsTab = ProductsViewModel.ScalingTab;

        viewModel.Scaling.Ingredients.Select(i => i.Name).Should().Equal("Flour", "Syrup", "Sugar");

        // 900 g of flour a batch of 100: 2,000 g covers 2 batches, so 200, not 222.
        viewModel.Scaling.HaveIngredient = viewModel.Scaling.Ingredients[0];
        viewModel.Scaling.HaveAmount = 2000m;
        viewModel.Scaling.HaveMakes.Should().Be(200);
        viewModel.Scaling.HaveText.Should().Be("Makes 200 pcs, using 1,800 g of the 2,000 g.");

        // Sugar is in the syrup: 100 g a batch.
        viewModel.Scaling.HaveIngredient = viewModel.Scaling.Ingredients[2];
        viewModel.Scaling.HaveAmount = 150m;
        viewModel.Scaling.HaveMakes.Should().Be(100);

        viewModel.Scaling.HaveIngredient = viewModel.Scaling.Ingredients[0];
        viewModel.Scaling.HaveAmount = 500m;
        viewModel.Scaling.HaveMakes.Should().Be(0);
        viewModel.Scaling.HaveText.Should().Be("Not enough for one: one takes 900 g.");
        (await viewModel.Scaling.ScaleToHaveCommand.CanExecute.FirstAsync()).Should().BeFalse();

        viewModel.Scaling.HaveAmount = 2000m;
        await viewModel.Scaling.ScaleToHaveCommand.Execute();
        viewModel.Scaling.Units.Should().Be(200);
        viewModel.Scaling.Rows.Single().Detail.Should().Be("2 batches · 200 pcs");
        viewModel.History.IsDirty.Should().BeFalse("none of it is an edit");
    });

    [Fact]
    public void An_amount_is_typed_in_grams_or_one_of_the_ingredients_own_units()
    {
        var flour = new RawMaterial
        {
            Name = "Flour",
            Units =
            [
                new RawMaterialUnit { BaseUnitsPerUnit = 25000m, UnitOfMeasure = new UnitOfMeasure { Name = "bag", Abbreviation = "bag" } },
                new RawMaterialUnit { BaseUnitsPerUnit = 1000m, UnitOfMeasure = new UnitOfMeasure { Name = "kilogram", Abbreviation = "kg" } },
            ],
        };

        IngredientAmountUnit.For(flour).Should().Equal(new IngredientAmountUnit("g", 1m), new IngredientAmountUnit("kg", 1000m), new IngredientAmountUnit("bag", 25000m));
        IngredientAmountUnit.For(null).Should().Equal(new IngredientAmountUnit("g", 1m));
    }

    /// <summary>
    /// Ensaymada: a Dough of 900 g of flour at 0.05 and 100 g of syrup, cut 10 g a piece, so 100 a batch. The syrup is
    /// made from sugar at 0.09, with no weight loss.
    /// </summary>
    private async Task<int> AddEnsaymadaAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var syrupId = await AddMadeIngredientAsync("Syrup", [(sugarId, 1000m)]);
        var (productId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 900m), (syrupId, 100m)], weightPerPiece: 10m);
        return productId;
    }

    private async Task<int> RoleIdAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        return dbContext.SubAssemblyRoles.Where(r => r.Name == name).Select(r => r.Id).Single();
    }

    private async Task<int> IngredientIdAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        return dbContext.RawMaterials.Where(m => m.Name == name).Select(m => m.Id).Single();
    }

    /// <summary>The Products screen, loaded, with the product open.</summary>
    private async Task<ProductsViewModel> OpenAsync(int productId, Func<ProductsViewModel, bool>? ready = null)
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && (ready?.Invoke(viewModel) ?? viewModel.UsageForm.FormLines.Count == 2),
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.UsageEdits.Count}, lines={viewModel.UsageForm.FormLines.Count}");
        return viewModel;
    }

    private ProductsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);

        return new ProductsViewModel(
            new ProductRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            packagingLineRepository,
            new CostingParametersRepository(factory),
            new CostingDefaultsRepository(factory),
            new PackagingLinkRepository(factory),
            new ProductCloner(factory),
            CreateUnitOfWork(),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new IngredientBomLineRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new ProductCategoryRepository(factory),
            new ProductVersionRepository(factory),
            new ProductValidator(),
            new CostingParametersValidator(),
            new ProductCategoryValidator(),
            new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
    }

    /// <summary>Selecting a product loads its detail in the background: waits until <paramref name="condition"/> has held for a moment.</summary>
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

        throw new TimeoutException($"The screen never settled: {describe()}.");
    }
}
