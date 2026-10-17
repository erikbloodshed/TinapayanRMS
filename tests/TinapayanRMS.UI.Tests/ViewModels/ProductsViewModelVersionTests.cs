using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
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
/// A product's version history on the Products screen: Save records a version when the recipe or price changes (and,
/// the first time, how the product stood before), the History tab shows each one priced then and today with what
/// changed in it, a note typed there is saved, and Restore puts a version into the form as unsaved edits Undo takes back.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductsViewModelVersionTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task The_first_Save_records_how_the_product_was_and_how_it_is()
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();

        Edit(viewModel, () => viewModel.FormLines.Single().Grams = 50m);
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        var versions = await VersionsAsync(seeded.ProductId);
        versions.Select(v => v.Number).Should().Equal(2, 1);
        versions[1].Note.Should().Be(ProductVersionRules.BaselineNote);
        GramsOf(versions[1]).Should().Be(100m, "the first is the product as it stood before this Save");
        GramsOf(versions[0]).Should().Be(50m);
    }

    [Fact]
    public async Task A_rename_alone_records_no_new_version()
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();

        Edit(viewModel, () => viewModel.EditingName = "Ube Roll Deluxe");
        await viewModel.SaveCommand.Execute();
        Edit(viewModel, () => viewModel.EditingName = "Ube Roll Supreme");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        (await VersionsAsync(seeded.ProductId)).Should().ContainSingle("only how it stood before the first Save: no recipe or price changed");
    }

    [Fact]
    public async Task A_price_change_records_a_version()
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();

        Edit(viewModel, () => viewModel.EditingCurrentPrice = 25m);
        await viewModel.SaveCommand.Execute();

        var latest = (await VersionsAsync(seeded.ProductId))[0];
        latest.Number.Should().Be(2);
        ProductVersionContent.FromJson(latest.Content).CurrentPrice.Should().Be(25m);
    }

    [Fact]
    public async Task The_History_tab_shows_each_version_then_and_today_and_what_changed()
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();
        Edit(viewModel, () => viewModel.FormLines.Single().Grams = 50m);
        await viewModel.SaveCommand.Execute();

        // Sugar doubles after both were saved: the versions' costs then stay, today's follow.
        await SetCostAsync(seeded.SugarId, 0.2m);
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedDetailsTab = ProductsViewModel.HistoryTab;
        await WaitUntilAsync(() => viewModel.VersionHistory.Rows.Count == 2, () => $"rows={viewModel.VersionHistory.Rows.Count}");

        var (latest, first) = (viewModel.VersionHistory.Rows[0], viewModel.VersionHistory.Rows[1]);
        first.BaseCostThen.Should().Be(10m, "100 g of sugar at 0.10, the whole batch one piece");
        first.BaseCostToday.Should().Be(20m, "the same 100 g at today's 0.20");
        latest.BaseCostThen.Should().Be(5m);
        latest.BaseCostToday.Should().Be(10m);

        viewModel.VersionHistory.SelectedRow.Should().BeSameAs(latest, "the newest is picked first");
        viewModel.VersionHistory.Changes.Should().Equal("Topping: Sugar 100 g → 50 g");

        viewModel.VersionHistory.SelectedRow = first;
        viewModel.VersionHistory.CompareIndex = ProductVersionHistory.CompareWithCurrent;
        viewModel.VersionHistory.Changes.Should().Equal("Topping: Sugar 100 g → 50 g");
    }

    [Fact]
    public async Task Restore_puts_a_version_into_the_form_as_unsaved_edits_and_Undo_takes_it_back()
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();
        var toppingId = viewModel.UsageEdits.Single().SubAssemblyId;
        Edit(viewModel, () => viewModel.FormLines.Single().Grams = 50m);
        Edit(viewModel, () => viewModel.EditingCurrentPrice = 25m);
        await viewModel.SaveCommand.Execute();
        await OpenHistoryAsync(viewModel, rows: 2);

        viewModel.VersionHistory.SelectedRow = viewModel.VersionHistory.Rows[1];
        await viewModel.RestoreVersionCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().StartWith("Version 1 is in the form.");
        var topping = viewModel.UsageEdits.Single();
        topping.Lines.Single().Grams.Should().Be(100m);
        topping.SubAssemblyId.Should().Be(toppingId, "the product still owns it, so Save rewrites it in place");
        viewModel.IsUsageFormOpen.Should().BeTrue("the section's tab shows its form, as it does when a product opens");
        viewModel.FormLines.Single().Grams.Should().Be(100m, "the form shows the restored lines, not an empty batch");
        viewModel.EditingCurrentPrice.Should().BeNull();
        viewModel.History.IsDirty.Should().BeTrue("it's unsaved until Save");

        viewModel.TryUndo().Should().BeTrue();
        viewModel.UsageEdits.Single().Lines.Single().Grams.Should().Be(50m);
        viewModel.EditingCurrentPrice.Should().Be(25m);
        viewModel.History.IsDirty.Should().BeFalse();

        viewModel.TryRedo().Should().BeTrue();
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblyLines.SingleAsync(l => l.SubAssemblyId == toppingId, Cancellation)).Grams.Should().Be(100m);
        var versions = await VersionsAsync(seeded.ProductId);
        versions.Select(v => v.Number).Should().Equal(3, 2, 1);
        ProductVersionRules.IsSameRecipe(ProductVersionContent.FromJson(versions[0].Content), ProductVersionContent.FromJson(versions[2].Content))
            .Should().BeTrue("the restored version saved again is the first one's recipe and price");
    }

    [Fact]
    public async Task Print_on_the_History_tab_prints_the_version_picked_at_its_costs_then()
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();
        Edit(viewModel, () => viewModel.FormLines.Single().Grams = 50m);
        await viewModel.SaveCommand.Execute();
        await SetCostAsync(seeded.SugarId, 0.2m);
        await viewModel.LoadCommand.Execute();
        await OpenHistoryAsync(viewModel, rows: 2);
        viewModel.VersionHistory.SelectedRow = viewModel.VersionHistory.Rows[1];

        var (printout, problem) = viewModel.PreparePrintout();

        problem.Should().BeNull();
        printout!.ProductName.Should().Be("Ube Roll (Version 1)");
        printout.SaveNote.Should().Be(ProductPrintout.VersionNote);
        printout.LastUpdatedText.Should().Be(viewModel.VersionHistory.Rows[1].SavedText);
        printout.Sections.Should().ContainSingle();
        printout.Costs.BaseCost.Should().Be(10m, "100 g of sugar at its cost then, 0.10 a gram");
        viewModel.FormLines.Single().Grams.Should().Be(50m, "printing a version leaves the form as it is");
    }

    [Fact]
    public async Task Print_on_the_History_tab_with_no_version_picked_says_so()
    {
        var (viewModel, _) = await OpenToppedProductAsync();
        await OpenHistoryAsync(viewModel, rows: 0);

        await viewModel.PrintCommand.Execute();

        viewModel.ErrorMessage.Should().Be("Pick a version in the History tab's table to print it.");
    }

    [Fact]
    public async Task Restore_links_a_linked_sub_assembly_again_as_it_is_now()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var subAssemblies = new SubAssemblyRepository(CreateDbContextFactory());
        var doughId = await subAssemblies.SaveStandaloneAsync(
            new SubAssembly { Name = "House Dough", RoleId = doughRoleId }, [(flourId, 1000m)], Cancellation);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 50m)]));
        var viewModel = await OpenAsync(productId, vm => vm.UsageEdits.Count == 1);

        Edit(viewModel, () => viewModel.EditingUsageWeightPerPiece = 40m);
        await viewModel.SaveCommand.Execute();
        await subAssemblies.SaveStandaloneAsync(
            new SubAssembly { Id = doughId, Name = "House Dough", RoleId = doughRoleId }, [(flourId, 800m)], Cancellation);
        await OpenHistoryAsync(viewModel, rows: 2);

        viewModel.VersionHistory.SelectedRow = viewModel.VersionHistory.Rows[1];
        await viewModel.RestoreVersionCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        var dough = viewModel.UsageEdits.Single();
        dough.SubAssemblyId.Should().Be(doughId);
        dough.IsLinked.Should().BeTrue();
        dough.AuthoredValue.Should().Be(50m, "the piece size is the product's own, and comes back");
        dough.Lines.Single().Grams.Should().Be(800m, "a version's lines are never written into a sub-assembly others may share");
        viewModel.SuccessMessage.Should().Contain("House Dough has changed on Sub-Assemblies since");
    }

    [Fact]
    public async Task A_version_saved_from_another_tab_shows_when_History_is_shown_again()
    {
        var (viewModel, _) = await OpenToppedProductAsync();
        Edit(viewModel, () => viewModel.EditingCurrentPrice = 25m);
        await viewModel.SaveCommand.Execute();
        await OpenHistoryAsync(viewModel, rows: 2);

        viewModel.SelectedDetailsTab = ProductsViewModel.RecipeTab;
        Edit(viewModel, () => viewModel.EditingCurrentPrice = 30m);
        await viewModel.SaveCommand.Execute();
        await OpenHistoryAsync(viewModel, rows: 3);

        viewModel.VersionHistory.Rows[0].CurrentPrice.Should().Be(30m);
    }

    [Fact]
    public async Task Restoring_a_version_the_same_as_the_saved_product_leaves_nothing_to_save()
    {
        var (viewModel, _) = await OpenToppedProductAsync();
        Edit(viewModel, () => viewModel.EditingCurrentPrice = 25m);
        await viewModel.SaveCommand.Execute();
        await OpenHistoryAsync(viewModel, rows: 2);
        viewModel.IsUsageFormOpen.Should().BeTrue("the section's form shows, as it does whenever a product opens");

        viewModel.VersionHistory.SelectedRow = viewModel.VersionHistory.Rows[0];
        await viewModel.RestoreVersionCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.History.IsDirty.Should().BeFalse("closing the section's form on the way isn't a change to save");
    }

    [Fact]
    public async Task Restore_is_refused_while_an_ingredient_it_needs_is_gone()
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.3m);
        await viewModel.LoadCommand.Execute();
        Edit(viewModel, () => viewModel.FormLines.Add(new IngredientLineRow { IngredientId = butterId, Grams = 20m }));
        await viewModel.SaveCommand.Execute();
        Edit(viewModel, () => viewModel.FormLines.RemoveAt(1));
        await viewModel.SaveCommand.Execute();
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.RawMaterials.Where(m => m.Id == butterId).ExecuteDeleteAsync(Cancellation);
        }

        await viewModel.LoadCommand.Execute();
        await OpenHistoryAsync(viewModel, rows: 3);
        viewModel.VersionHistory.SelectedRow = viewModel.VersionHistory.Rows[1];
        await viewModel.RestoreVersionCommand.Execute();

        viewModel.ErrorMessage.Should().Be("Version 2 can't be restored: it needs Butter, which is no longer there.");
        viewModel.UsageEdits.Single().Lines.Should().ContainSingle("nothing changed");
        viewModel.History.CanUndo.Should().BeFalse();
        _ = seeded;
    }

    [Fact]
    public async Task A_note_typed_into_a_version_is_saved()
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();
        Edit(viewModel, () => viewModel.EditingCurrentPrice = 25m);
        await viewModel.SaveCommand.Execute();
        await OpenHistoryAsync(viewModel, rows: 2);

        viewModel.VersionHistory.Rows[0].Note = "  New price for October ";
        await WaitUntilAsync(
            () => viewModel.VersionHistory.Rows[0].Note == "New price for October",
            () => $"note={viewModel.VersionHistory.Rows[0].Note}, error={viewModel.ErrorMessage}");

        (await VersionsAsync(seeded.ProductId))[0].Note.Should().Be("New price for October");
    }

    private static void Edit(ProductsViewModel viewModel, Action change)
    {
        var recorder = (IUndoRecorder)viewModel;
        recorder.BeginUndoStep();
        change();
        recorder.EndUndoStep();
    }

    private static decimal GramsOf(ProductVersion version) =>
        ProductVersionContent.FromJson(version.Content).Sections.Single().Lines.Single().Grams;

    private Task<IReadOnlyList<ProductVersion>> VersionsAsync(int productId) =>
        new ProductVersionRepository(CreateDbContextFactory()).GetForProductAsync(productId, Cancellation);

    private async Task SetCostAsync(int rawMaterialId, decimal costPerGram)
    {
        await using var dbContext = CreateDbContext();
        await dbContext.RawMaterials.Where(m => m.Id == rawMaterialId).ExecuteUpdateAsync(
            s => s.SetProperty(m => m.CostPerBaseUnit, costPerGram).SetProperty(m => m.BufferedCostPerBaseUnit, costPerGram),
            Cancellation);
    }

    private sealed record Seeded(int ProductId, int SugarId);

    /// <summary>
    /// A saved product whose only section is its own Topping of 100 g of sugar at 0.10 a gram, the whole batch one piece,
    /// opened with its form showing. A product-owned role, so the section is the product's own.
    /// </summary>
    private async Task<(ProductsViewModel ViewModel, Seeded Seeded)> OpenToppedProductAsync()
    {
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 1, isProductOwned: true);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.1m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(toppingRoleId, [(sugarId, 100m)], UsageAuthoredField.Yield, 1m)], []));

        var viewModel = await OpenAsync(productId, vm => vm.FormLines.Count == 1);
        return (viewModel, new Seeded(productId, sugarId));
    }

    private static async Task OpenHistoryAsync(ProductsViewModel viewModel, int rows)
    {
        viewModel.SelectedDetailsTab = ProductsViewModel.HistoryTab;
        await WaitUntilAsync(
            () => viewModel.VersionHistory.Rows.Count == rows,
            () => $"rows={viewModel.VersionHistory.Rows.Count}, error={viewModel.ErrorMessage}");
    }

    private async Task<ProductsViewModel> OpenAsync(int productId, Func<ProductsViewModel, bool> ready)
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await WaitUntilAsync(
            () => viewModel.AreDetailsReady && ready(viewModel),
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.UsageEdits.Count}, lines={viewModel.FormLines.Count}");
        return viewModel;
    }

    private ProductsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();

        return new ProductsViewModel(
            new ProductRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            new PackagingLineRepository(factory),
            new CostingParametersRepository(factory),
            new CostingDefaultsRepository(factory),
            new PackagingLinkRepository(factory),
            new ProductCloner(factory),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new IngredientBomLineRepository(factory),
            new ProductCategoryRepository(factory),
            new ProductVersionRepository(factory),
            new ProductValidator(),
            new CostingParametersValidator(),
            new ProductCategoryValidator(),
            new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
    }

    private static async Task WaitUntilAsync(Func<bool> condition, Func<string> describe)
    {
        var deadline = Stopwatch.StartNew();
        var heldFor = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (!condition())
            {
                heldFor.Restart();
            }
            else if (heldFor.Elapsed > TimeSpan.FromMilliseconds(300))
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Never settled: {describe()}.");
    }
}
