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
/// The Print tab, on real repositories: This product is the Products screen's product as it shows it; a folder of the
/// Products list, or all products, print as they're saved, in the list's order (the user's choices, 2026-09-27). And a
/// saved product read for a set is the same printout the Products screen gives once it's opened.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class PrintTabViewModelTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task A_category_folder_prints_its_products_in_the_lists_order_under_its_title()
    {
        var (breads, _) = await AddCatalogueAsync();
        var tab = CreatePrintTab(await OpenProductsAsync());

        tab.Pick(PrintScope.Folder(ProductFolderNode.ForCategory(ProductStage.Production, breads)));
        await tab.LoadCommand.Execute();

        tab.PrintSet!.Title.Should().Be("Breads · Production");
        tab.PrintSet.Products.Select(p => p.ProductName).Should().Equal("Cheese Roll", "Pan de Coco");
        tab.PrintSet.Products.Should().OnlyContain(p => p.SaveNote == null, "a set prints as saved");
        tab.Note.Should().BeNull();
    }

    [Fact]
    public async Task Uncategorized_and_a_stage_hold_what_the_list_files_under_them()
    {
        await AddCatalogueAsync();
        var tab = CreatePrintTab(await OpenProductsAsync());

        tab.Pick(PrintScope.Folder(ProductFolderNode.Uncategorized(ProductStage.Production)));
        await tab.LoadCommand.Execute();
        tab.PrintSet!.Products.Select(p => p.ProductName).Should().Equal("Loaf");

        tab.Pick(PrintScope.Folder(ProductFolderNode.ForStage(ProductStage.ResearchAndDevelopment)));
        await tab.LoadCommand.Execute();
        tab.PrintSet!.Title.Should().Be("Research and Development");
        tab.PrintSet.Products.Select(p => p.ProductName).Should().Equal("Test Bread");
    }

    [Fact]
    public async Task All_products_print_Production_first_folder_by_folder()
    {
        await AddCatalogueAsync();
        var tab = CreatePrintTab(await OpenProductsAsync());

        tab.Pick(PrintScope.AllProducts);
        await tab.LoadCommand.Execute();

        tab.PrintSet!.Title.Should().Be("All Products");
        tab.PrintSet.Products.Select(p => p.ProductName).Should().Equal("Cheese Roll", "Pan de Coco", "Mocha Roll", "Loaf", "Test Bread");
        tab.Scopes.Select(s => s.Label).Should().Equal("This product", "All products");
    }

    [Fact]
    public async Task A_folder_asked_for_is_offered_beside_This_product_and_All_products_in_place_of_the_last()
    {
        var (breads, cakes) = await AddCatalogueAsync();
        var tab = CreatePrintTab(await OpenProductsAsync());

        tab.Pick(PrintScope.Folder(ProductFolderNode.ForCategory(ProductStage.Production, breads)));
        tab.Pick(PrintScope.Folder(ProductFolderNode.ForCategory(ProductStage.Production, cakes)));

        tab.Scopes.Select(s => s.Label).Should().Equal("This product", "Cakes · Production", "All products");
        tab.SelectedScope.Label.Should().Be("Cakes · Production");
    }

    [Fact]
    public async Task A_set_prints_the_open_product_as_saved_and_says_so_while_This_product_has_its_edits()
    {
        await AddCatalogueAsync();
        var products = await OpenProductsAsync("Cheese Roll");
        products.EditingCurrentPrice = 45m;
        var tab = CreatePrintTab(products);

        tab.Pick(PrintScope.AllProducts);
        await tab.LoadCommand.Execute();

        tab.PrintSet!.Products.Single(p => p.ProductName == "Cheese Roll").Costs.CurrentPrice.Should().BeNull("it isn't saved");
        tab.Note.Should().Be("Cheese Roll has unsaved changes on Products; it's printed as saved.");

        tab.Pick(PrintScope.ThisProduct);
        await tab.LoadCommand.Execute();

        tab.PrintSet!.Title.Should().BeNull();
        tab.PrintSet.Products.Single().Costs.CurrentPrice.Should().Be(45m);
        tab.PrintSet.Products.Single().SaveNote.Should().Be(ProductPrintout.UnsavedChangesNote);
        tab.Note.Should().BeNull();
    }

    [Fact]
    public async Task A_saved_product_reads_as_the_Products_screen_shows_it_once_opened()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new NewSubAssemblyDraft(fillingRoleId, [(sugarId, 200m)], UsageAuthoredField.Yield, 40m),
            ],
            []));
        await AddBoxAsync(productId);
        await SetCostingAsync(productId);
        var products = await OpenProductsAsync("Ube Roll", vm => vm.UsageEdits.Count == 2 && vm.HasPackaging);

        var screen = products.PreparePrintout().Printout!;
        var saved = (await CreateReader().ReadAsync(products.SelectedProduct!, Cancellation)).ToPrintout(screen.PrintedAt);

        saved.Should().BeEquivalentTo(screen);
        saved.Costs.SellingPrice.Should().BePositive();
    }

    [Fact]
    public async Task Unbuffered_a_set_prints_at_the_true_costs_as_This_product_does_and_says_so()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.04m, bufferedCostPerGram: 0.05m);
        var (productId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m)], weightPerPiece: 50m);
        await AddBoxAsync(productId, unbufferedCost: 25m);
        await SetCostingAsync(productId);
        var products = await OpenProductsAsync("Ensaymada", vm => vm.UsageEdits.Count == 1 && vm.HasPackaging);
        await products.ToggleCostBasisCommand.Execute();
        var tab = CreatePrintTab(products);

        tab.Pick(PrintScope.ThisProduct);
        await tab.LoadCommand.Execute();
        var screen = tab.PrintSet!.Products.Single();

        tab.Pick(PrintScope.AllProducts);
        await tab.LoadCommand.Execute();
        var saved = tab.PrintSet!.Products.Single();

        saved.CostBasis.Should().Be(CostBasis.Unbuffered);
        (saved.Costs.SubAssemblies, saved.Costs.Packaging).Should().Be((2.00m, 25m));
        saved.Should().BeEquivalentTo(screen, o => o.Excluding(p => p.PrintedAt));
        tab.Note.Should().BeNull("the open product has no edits: the switch isn't one");
    }

    [Fact]
    public async Task A_saved_composite_reads_as_the_Products_screen_shows_it_once_opened()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m)], weightPerPiece: 50m);
        var (cheeseRollId, _) = await AddProductWithSubAssemblyAsync("Cheese Roll", doughRoleId, [(flourId, 1000m)], weightPerPiece: 100m);
        var boxId = await new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = "Pasalubong Box", IsComposite = true },
            new RecipeDraft([], []) { Components = [(ensaymadaId, 6), (cheeseRollId, 4)] },
            null,
            null,
            null,
            Cancellation);
        await AddBoxAsync(boxId);
        await SetCostingAsync(boxId);
        var products = await OpenProductsAsync("Pasalubong Box", vm => vm.ComponentEdits.Count == 2 && vm.HasPackaging);

        var screen = products.PreparePrintout().Printout!;
        var saved = (await CreateReader().ReadAsync(products.SelectedProduct!, Cancellation)).ToPrintout(screen.PrintedAt);

        saved.Should().BeEquivalentTo(screen);
        saved.Components.Should().HaveCount(2);
        saved.Costs.SubAssemblies.Should().Be(35m);
        saved.Costs.Packaging.Should().Be(30m);
    }

    /// <summary>
    /// Two categories and five products: Production's Breads (Pan de Coco, Cheese Roll), Cakes (Mocha Roll) and Uncategorized
    /// (Loaf), and Research and Development's Breads (Test Bread). Named out of order, so the list's order is the test's.
    /// </summary>
    private async Task<(ProductCategory Breads, ProductCategory Cakes)> AddCatalogueAsync()
    {
        await using var dbContext = CreateDbContext();
        var cakes = new ProductCategory { Name = "Cakes" };
        var breads = new ProductCategory { Name = "Breads" };
        dbContext.ProductCategories.AddRange(cakes, breads);
        dbContext.Products.AddRange(
            new Product { Name = "Pan de Coco", Stage = ProductStage.Production, Category = breads },
            new Product { Name = "Test Bread", Stage = ProductStage.ResearchAndDevelopment, Category = breads },
            new Product { Name = "Mocha Roll", Stage = ProductStage.Production, Category = cakes },
            new Product { Name = "Loaf", Stage = ProductStage.Production },
            new Product { Name = "Cheese Roll", Stage = ProductStage.Production, Category = breads });
        await dbContext.SaveChangesAsync(Cancellation);
        return (breads, cakes);
    }

    /// <summary>A box a piece, 30 buffered and <paramref name="unbufferedCost"/> (30 unless given) true, saved straight to the product.</summary>
    private async Task AddBoxAsync(int productId, decimal unbufferedCost = 30m)
    {
        await using var dbContext = CreateDbContext();
        var box = new RawMaterial
        {
            Name = "Gift Box", Type = RawMaterialType.Packaging, Category = new RawMaterialCategory { Name = "Boxes" },
            BaseUnit = new UnitOfMeasure { Name = "piece", Abbreviation = "pc" },
            CostPerBaseUnit = unbufferedCost, BufferedCostPerBaseUnit = 30m,
        };
        dbContext.RawMaterials.Add(box);
        await dbContext.SaveChangesAsync(Cancellation);

        dbContext.PackagingLines.Add(new PackagingLine
        {
            ProductId = productId, PackagingMaterialId = box.Id, AuthoredField = PackagingLineAuthoredField.QtyPerUnit, QtyPerUnit = 1m,
        });
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private async Task SetCostingAsync(int productId)
    {
        await using var dbContext = CreateDbContext();
        dbContext.CostingParameters.Add(new CostingParameters
        {
            ProductId = productId, LaborPercent = 25m, OverheadPercent = 10m, AdministrativePercent = 5m, MarginPercent = 30m, VatPercent = 12m,
        });
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private PrintTabViewModel CreatePrintTab(ProductsViewModel products)
    {
        var factory = CreateDbContextFactory();
        return new PrintTabViewModel(products, new ProductRepository(factory), CreateReader(), new LoggerConfiguration().CreateLogger());
    }

    private SavedProductReader CreateReader()
    {
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);
        return new SavedProductReader(
            new PackagingLinkRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            packagingLineRepository,
            new CostingParametersRepository(factory),
            new SubAssemblyLineRepository(factory),
            new ProductRepository(factory),
            new CostingDefaultsRepository(factory));
    }

    /// <summary>The Products screen, loaded, with <paramref name="productName"/> open when one is given.</summary>
    private async Task<ProductsViewModel> OpenProductsAsync(string? productName = null, Func<ProductsViewModel, bool>? ready = null)
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        if (productName is not null)
        {
            viewModel.SelectedProduct = viewModel.Products.Single(p => p.Name == productName);
            await WaitUntilSettledAsync(
                () => viewModel.AreDetailsReady && (ready?.Invoke(viewModel) ?? true),
                () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.UsageEdits.Count}");
        }

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

        throw new TimeoutException($"The product's detail never finished loading: {describe()}.");
    }
}
