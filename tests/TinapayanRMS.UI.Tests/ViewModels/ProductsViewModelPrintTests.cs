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
/// Print on the Products screen, on real repositories: what the Print tab shows is the product as the screen shows it,
/// unsaved edits included, with a form left open applied first, as Save applies it (the user's choice, 2026-09-27).
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductsViewModelPrintTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task Print_shows_the_product_as_the_screen_shows_it()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new NewSubAssemblyDraft(fillingRoleId, [(sugarId, 200m)], UsageAuthoredField.WeightPerPiece, 10m),
            ],
            []));
        await AddPackagingLineAsync(productId);
        var viewModel = await OpenAsync(productId, vm => vm.UsageEdits.Count == 2 && vm.HasPackaging);

        var printout = await PrintAsync(viewModel);

        printout.ProductName.Should().Be("Ube Roll");
        printout.CategoryName.Should().Be("Uncategorized");
        printout.StageTitle.Should().Be("Research and Development");
        printout.CostUnitCaption.Should().Be("Per piece");
        printout.SaveNote.Should().BeNull("nothing was changed since it was saved");

        printout.Sections.Select(s => s.Name).Should().Equal("Dough", "Filling");
        printout.Sections[0].Lines.Should().Equal(new PrintedLine("Flour", 1000m, 100m, CostPerGram: 0.05m, 50m, IsFormulaBase: false));
        printout.Sections[1].Lines.Should().Equal(new PrintedLine("Sugar", 200m, 100m, CostPerGram: 0.09m, 18m, IsFormulaBase: false));
        (printout.Sections[0].WeightPerPiece, printout.Sections[0].Yield).Should().Be((50m, 20m));

        printout.Packaging.Should().NotBeNull();
        printout.Packaging!.Lines.Should().Equal(new PrintedPackagingLine("Gift Box", 1m, "pc", 30m, 30m));
        printout.Costs.Should().BeSameAs(viewModel.Costing.Costs, "the costing is the Costing tab's");
        printout.Scaling.Should().BeNull("the Recipe tab was showing");
    }

    [Fact]
    public async Task Print_on_the_Scaling_tab_prints_the_recipe_scaled_and_on_another_tab_its_recipe_and_costing_again()
    {
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();
        viewModel.EditingQuantityRequired = 8;
        viewModel.ScalingUnits = 5;
        viewModel.SelectedDetailsTab = ProductsViewModel.ScalingTab;

        var printout = await PrintAsync(viewModel);

        // 5 boxes of 8 are 40 pieces: 2 batches of the 20 50 g pieces a 1,000 g Dough makes.
        printout.Scaling.Should().NotBeNull();
        var scaling = printout.Scaling!;
        (scaling.Units, scaling.Pieces, scaling.TotalCost).Should().Be((5, 40m, 100m));
        scaling.Rows.Should().Equal(viewModel.ScalingRows, "the pages are the tab as it shows");
        scaling.Rows.Single().Detail.Should().Be("2 batches · 40 pcs");

        // The Print tab, shown again with the Recipe tab showing on Products, follows it.
        viewModel.SelectedDetailsTab = ProductsViewModel.RecipeTab;
        viewModel.PreparePrintout().Printout!.Scaling.Should().BeNull();
    }

    [Fact]
    public async Task Print_applies_a_line_typed_into_the_open_form_and_notes_the_unsaved_change()
    {
        var (viewModel, sugarId) = await OpenOneSubAssemblyProductAsync();
        viewModel.IsUsageFormOpen.Should().BeTrue();
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = sugarId, Grams = 500m });

        var printout = await PrintAsync(viewModel);

        printout.Sections.Single().Lines.Select(l => (l.Ingredient, l.Grams)).Should().Equal(("Flour", 1000m), ("Sugar", 500m));
        printout.SaveNote.Should().Be(ProductPrintout.UnsavedChangesNote);
        viewModel.UsageEdits.Single().Lines.Should().HaveCount(2, "the form was applied, as Save applies it");
        viewModel.IsUsageFormOpen.Should().BeTrue("its tab shows it again, as after a Save");

        // Applying it was one step, which Undo takes back.
        viewModel.TryUndo().Should().BeTrue();
        viewModel.UsageEdits.Single().Lines.Should().ContainSingle();
    }

    [Fact]
    public async Task A_form_a_rule_refuses_shows_why_and_nothing_opens()
    {
        var (viewModel, sugarId) = await OpenOneSubAssemblyProductAsync();
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = sugarId });
        var requests = 0;
        using var subscription = viewModel.PrintRequests.Subscribe(_ => requests++);

        await viewModel.PrintCommand.Execute();

        viewModel.ErrorMessage.Should().Contain("Sugar", "a line with no grams can't go into the recipe");
        requests.Should().Be(0, "the Print tab doesn't open");
        viewModel.UsageEdits.Single().Lines.Should().ContainSingle();
    }

    [Fact]
    public async Task The_Print_tab_shown_again_has_the_edits_made_on_Products_since()
    {
        var (viewModel, sugarId) = await OpenOneSubAssemblyProductAsync();
        (await PrintAsync(viewModel)).SaveNote.Should().BeNull();

        viewModel.EditingName = "Ube Roll Deluxe";
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = sugarId, Grams = 250m });
        var (printout, problem) = viewModel.PreparePrintout();

        problem.Should().BeNull();
        printout!.ProductName.Should().Be("Ube Roll Deluxe");
        printout.Sections.Single().Lines.Select(l => (l.Ingredient, l.Grams)).Should().Equal(("Flour", 1000m), ("Sugar", 250m));
        printout.SaveNote.Should().Be(ProductPrintout.UnsavedChangesNote);
    }

    [Fact]
    public async Task A_form_refused_when_the_Print_tab_is_shown_again_leaves_its_pages_and_says_why()
    {
        var (viewModel, sugarId) = await OpenOneSubAssemblyProductAsync();
        await PrintAsync(viewModel);

        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = sugarId });
        var (printout, problem) = viewModel.PreparePrintout();

        printout.Should().BeNull("the tab keeps the pages it has");
        problem.Should().Contain("Sugar");
        viewModel.ErrorMessage.Should().Be(problem, "Products shows it too, for when it's next shown");
    }

    [Fact]
    public async Task A_new_product_prints_as_not_saved_yet()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.EditingName = "Pandesal";

        var printout = await PrintAsync(viewModel);

        printout.ProductName.Should().Be("Pandesal");
        printout.SaveNote.Should().Be(ProductPrintout.NotSavedNote);
        printout.Sections.Should().BeEmpty();
        printout.Packaging.Should().BeNull();
    }

    /// <summary>Runs Print, then asks for the printout as the main window's Print tab does once Print has asked for it.</summary>
    private static async Task<ProductPrintout> PrintAsync(ProductsViewModel viewModel)
    {
        var requests = 0;
        using (viewModel.PrintRequests.Subscribe(_ => requests++))
        {
            await viewModel.PrintCommand.Execute();
        }

        viewModel.ErrorMessage.Should().BeNull();
        requests.Should().Be(1, "Print asks for the Print tab");
        var (printout, problem) = viewModel.PreparePrintout();
        problem.Should().BeNull();
        return printout ?? throw new InvalidOperationException("The Print tab got no printout.");
    }

    /// <summary>A saved product with one Dough of 1,000 g of flour, opened with its form showing, and the id of an ingredient it doesn't use.</summary>
    private async Task<(ProductsViewModel ViewModel, int SugarId)> OpenOneSubAssemblyProductAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m)], []));

        return (await OpenAsync(productId, vm => vm.FormLines.Count == 1), sugarId);
    }

    /// <summary>A box a piece, saved straight to the product.</summary>
    private async Task AddPackagingLineAsync(int productId)
    {
        await using var dbContext = CreateDbContext();
        var box = new RawMaterial
        {
            Name = "Gift Box", Type = RawMaterialType.Packaging, Category = new RawMaterialCategory { Name = "Boxes" },
            BaseUnit = new UnitOfMeasure { Name = "piece", Abbreviation = "pc" },
            CostPerBaseUnit = 30m, BufferedCostPerBaseUnit = 30m,
        };
        dbContext.RawMaterials.Add(box);
        await dbContext.SaveChangesAsync(Cancellation);

        dbContext.PackagingLines.Add(new PackagingLine
        {
            ProductId = productId, PackagingMaterialId = box.Id, AuthoredField = PackagingLineAuthoredField.QtyPerUnit, QtyPerUnit = 1m,
        });
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private async Task<ProductsViewModel> OpenAsync(int productId, Func<ProductsViewModel, bool> ready)
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && ready(viewModel),
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.UsageEdits.Count}, lines={viewModel.FormLines.Count}");
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
