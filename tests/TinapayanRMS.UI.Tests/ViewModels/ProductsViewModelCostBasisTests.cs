using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
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
/// The Products screen's Unbuffered button, on real repositories: every figure moves between the materials' buffered
/// costs and their true ones, the rows, sections, packaging, a composite's products and the printout alike. It's no
/// edit, so there's nothing to undo or save, and it stays put from one product to the next.
/// </summary>
[Collection(DatabaseCollection.A)]
public class ProductsViewModelCostBasisTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public Task Unbuffered_prices_the_whole_screen_at_the_true_costs_and_changes_nothing_about_the_product() => StaThread.RunAsync(async () =>
    {
        var productId = await AddEnsaymadaAsync();
        var viewModel = await OpenAsync(productId);
        viewModel.CostBasis.Should().Be(CostBasis.Buffered, "the screen starts on the buffered costs");
        viewModel.CostBasisButtonLabel.Should().Be("Unbuffered");
        (viewModel.Costing.Costs.SubAssemblies, viewModel.Costing.Costs.Packaging).Should().Be((2.50m, 30m));
        viewModel.UsageForm.FormLines.Single().CostPerGram.Should().Be(0.05m);
        viewModel.UsageForm.UsageFormBatchCostText.Should().Be($"{50m:N2}");

        await viewModel.ToggleCostBasisCommand.Execute();

        viewModel.IsUnbuffered.Should().BeTrue();
        viewModel.CostBasisButtonLabel.Should().Be("Buffered", "the button names the costs a click switches to");
        (viewModel.Costing.Costs.SubAssemblies, viewModel.Costing.Costs.Packaging).Should().Be((2.00m, 25m), "50 g of 1,000 g of flour at 0.04, and a box at 25");
        viewModel.UsageForm.FormLines.Single().CostPerGram.Should().Be(0.04m);
        viewModel.Packaging.LineEdits.Single().UnitCost.Should().Be(25m);
        viewModel.UsageForm.UsageFormBatchCostText.Should().Be($"{40m:N2}");
        viewModel.UsageForm.NewFormLine().CostBasis.Should().Be(CostBasis.Unbuffered, "a row started in the add row is priced the same");
        viewModel.Packaging.NewLineEdit().CostBasis.Should().Be(CostBasis.Unbuffered);
        viewModel.History.IsDirty.Should().BeFalse("the switch isn't an edit");
        viewModel.History.CanUndo.Should().BeFalse();

        var printout = viewModel.PreparePrintout().Value.Should().BeOfType<ProductPrintout>().Subject;
        printout.CostBasis.Should().Be(CostBasis.Unbuffered);
        printout.Sections.Single().CostPerPiece.Should().Be(2.00m);
        printout.Sections.Single().Lines.Single().CostPerGram.Should().Be(0.04m);
        printout.Packaging!.Total.Should().Be(25m);

        await viewModel.ToggleCostBasisCommand.Execute();

        viewModel.IsUnbuffered.Should().BeFalse();
        viewModel.CostBasisButtonLabel.Should().Be("Unbuffered");
        (viewModel.Costing.Costs.SubAssemblies, viewModel.Costing.Costs.Packaging).Should().Be((2.50m, 30m));
        viewModel.UsageForm.FormLines.Single().CostPerGram.Should().Be(0.05m);
        viewModel.PreparePrintout().Value.Should().BeOfType<ProductPrintout>().Which.CostBasis.Should().Be(CostBasis.Buffered);
    });

    [Fact]
    public Task Rows_an_undo_puts_back_are_priced_unbuffered_too() => StaThread.RunAsync(async () =>
    {
        var productId = await AddEnsaymadaAsync();
        var viewModel = await OpenAsync(productId);
        await viewModel.ToggleCostBasisCommand.Execute();

        Edit(viewModel, () => viewModel.UsageForm.FormLines.Single().Grams = 500m);
        viewModel.TryUndo().Should().BeTrue();

        viewModel.UsageForm.FormLines.Single().Should().Match<IngredientLineRow>(r => r.Grams == 1000m && r.CostBasis == CostBasis.Unbuffered);
        viewModel.UsageForm.FormLines.Single().Cost.Should().Be(40m);
        viewModel.Costing.Costs.SubAssemblies.Should().Be(2.00m);
    });

    [Fact]
    public Task Unbuffered_stays_on_for_the_next_product_picked() => StaThread.RunAsync(async () =>
    {
        var productId = await AddEnsaymadaAsync();
        var doughRoleId = await RoleIdAsync("Dough");
        var flourId = await IngredientIdAsync("Flour");
        var (cheeseRollId, _) = await AddProductWithSubAssemblyAsync("Cheese Roll", doughRoleId, [(flourId, 1000m)], weightPerPiece: 100m);
        var viewModel = await OpenAsync(productId);
        await viewModel.ToggleCostBasisCommand.Execute();

        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == cheeseRollId);
        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && viewModel.SelectedProduct?.Id == cheeseRollId && viewModel.UsageForm.FormLines.Count == 1,
            () => $"selected={viewModel.SelectedProduct?.Id}, lines={viewModel.UsageForm.FormLines.Count}");

        viewModel.IsUnbuffered.Should().BeTrue();
        viewModel.UsageForm.FormLines.Single().CostBasis.Should().Be(CostBasis.Unbuffered);
        viewModel.Costing.Costs.SubAssemblies.Should().Be(4.00m, "100 g of 1,000 g of flour at 0.04");
    });

    [Fact]
    public Task A_composites_products_bring_their_unbuffered_recipe_costs() => StaThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.04m, bufferedCostPerGram: 0.05m);
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m)], weightPerPiece: 50m);
        var (cheeseRollId, _) = await AddProductWithSubAssemblyAsync("Cheese Roll", doughRoleId, [(flourId, 1000m)], weightPerPiece: 100m);
        var boxId = await new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = "Pasalubong Box", IsComposite = true },
            new RecipeDraft([], []) { Components = [(ensaymadaId, 6), (cheeseRollId, 4)] },
            null,
            null,
            null,
            Cancellation);
        var viewModel = await OpenAsync(boxId, vm => vm.ComponentsTable.ComponentEdits.Count == 2 && vm.ComponentsTable.ComponentEdits.All(r => r.RecipeCost is not null));
        viewModel.Costing.Costs.SubAssemblies.Should().Be(35m, "6 × 2.50 + 4 × 5.00");

        await viewModel.ToggleCostBasisCommand.Execute();

        viewModel.ComponentsTable.ComponentEdits.Select(r => r.RecipeCost).Should().Equal(2.00m, 4.00m);
        viewModel.Costing.Costs.SubAssemblies.Should().Be(28m, "6 × 2.00 + 4 × 4.00");
        viewModel.PreparePrintout().Value.Should().BeOfType<ProductPrintout>().Which.Components!.Select(c => c.Cost).Should().Equal(12m, 16m);
        viewModel.History.IsDirty.Should().BeFalse();
    });

    /// <summary>
    /// Ensaymada: a Dough of 1,000 g of flour, 0.05 a gram buffered and 0.04 true, 50 g a piece (2.50 buffered, 2.00
    /// true), and a gift box a piece, 30 buffered and 25 true.
    /// </summary>
    // Every cost on the screen is at buffered costs, and a purchased material's is a fixed amount that rising prices can
    // pass: the product was then priced under what it cost with nothing here to say so (2026-11-05).
    [Fact]
    public Task The_header_says_when_prices_have_risen_past_a_materials_buffered_cost() => StaThread.RunAsync(async () =>
    {
        var productId = await AddEnsaymadaAsync();
        var viewModel = await OpenAsync(productId);
        viewModel.BelowCostText.Should().BeNull("the flour and the box are costed above what they cost");

        // The flour's price goes past its buffered cost, as a new brand price leaves it; then the box's too.
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.RawMaterials.Where(m => m.Name == "Flour").ExecuteUpdateAsync(set => set.SetProperty(m => m.CostPerBaseUnit, 0.06m), Cancellation);
        }

        (await OpenAsync(productId)).BelowCostText.Should().Be(
            "Prices have risen past the buffered cost of Flour, so this product is priced under what it costs. "
            + "Re-apply its target buffer on Raw Materials, or in Reports' Cost Buffers.");

        await using (var dbContext = CreateDbContext())
        {
            await dbContext.RawMaterials.Where(m => m.Name == "Gift Box").ExecuteUpdateAsync(set => set.SetProperty(m => m.CostPerBaseUnit, 36m), Cancellation);
        }

        (await OpenAsync(productId)).BelowCostText.Should().Be(
            "Prices have risen past the buffered costs of Flour and Gift Box, so this product is priced under what they cost. "
            + "Re-apply their target buffers on Raw Materials, or in Reports' Cost Buffers.");
    });

    private async Task<int> AddEnsaymadaAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.04m, bufferedCostPerGram: 0.05m);
        var (productId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m)], weightPerPiece: 50m);

        await using var dbContext = CreateDbContext();
        var box = new RawMaterial
        {
            Name = "Gift Box", Type = RawMaterialType.Packaging, Category = new RawMaterialCategory { Name = "Boxes" },
            BaseUnit = new UnitOfMeasure { Name = "piece", Abbreviation = "pc" },
            CostPerBaseUnit = 25m, BufferedCostPerBaseUnit = 30m,
        };
        dbContext.RawMaterials.Add(box);
        await dbContext.SaveChangesAsync(Cancellation);
        dbContext.PackagingLines.Add(new PackagingLine
        {
            ProductId = productId, PackagingMaterialId = box.Id, AuthoredField = PackagingLineAuthoredField.QtyPerUnit, QtyPerUnit = 1m,
        });
        await dbContext.SaveChangesAsync(Cancellation);
        return productId;
    }

    private async Task<int> RoleIdAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblyRoles.Where(r => r.Name == name).Select(r => r.Id).SingleAsync(Cancellation);
    }

    private async Task<int> IngredientIdAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterials.Where(m => m.Name == name).Select(m => m.Id).SingleAsync(Cancellation);
    }

    private static void Edit(ProductsViewModel viewModel, Action change)
    {
        var recorder = (IUndoRecorder)viewModel;
        recorder.BeginUndoStep();
        change();
        recorder.EndUndoStep();
    }

    /// <summary>The Products screen, loaded, with the product open and its first section's form showing.</summary>
    private async Task<ProductsViewModel> OpenAsync(int productId, Func<ProductsViewModel, bool>? ready = null)
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && (ready?.Invoke(viewModel) ?? (viewModel.UsageForm.FormLines.Count == 1 && viewModel.HasPackaging)),
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
}
