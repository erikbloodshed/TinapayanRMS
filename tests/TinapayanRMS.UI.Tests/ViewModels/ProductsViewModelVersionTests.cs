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
[Collection(DatabaseCollection.D)]
public class ProductsViewModelVersionTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public Task The_first_Save_records_how_the_product_was_and_how_it_is() => StaThread.RunAsync(async () =>
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();

        Edit(viewModel, () => viewModel.UsageForm.FormLines.Single().Grams = 50m);
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        var versions = await VersionsAsync(seeded.ProductId);
        versions.Select(v => v.Number).Should().Equal(2, 1);
        versions[1].Note.Should().Be(ProductVersionRules.BaselineNote);
        GramsOf(versions[1]).Should().Be(100m, "the first is the product as it stood before this Save");
        GramsOf(versions[0]).Should().Be(50m);
    });

    [Fact]
    public Task A_rename_alone_records_no_new_version() => StaThread.RunAsync(async () =>
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();

        Edit(viewModel, () => viewModel.EditingName = "Ube Roll Deluxe");
        await viewModel.SaveCommand.Execute();
        Edit(viewModel, () => viewModel.EditingName = "Ube Roll Supreme");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        (await VersionsAsync(seeded.ProductId)).Should().ContainSingle("only how it stood before the first Save: no recipe or price changed");
    });

    [Fact]
    public Task A_price_change_records_a_version() => StaThread.RunAsync(async () =>
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();

        Edit(viewModel, () => viewModel.EditingCurrentPrice = 25m);
        await viewModel.SaveCommand.Execute();

        var latest = (await VersionsAsync(seeded.ProductId))[0];
        latest.Number.Should().Be(2);
        ProductVersionContent.FromJson(latest.Content).CurrentPrice.Should().Be(25m);
    });

    [Fact]
    public Task The_History_tab_shows_each_version_then_and_today_and_what_changed() => StaThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var (viewModel, seeded) = await OpenToppedProductAsync();
        Edit(viewModel, () => viewModel.UsageForm.FormLines.Single().Grams = 50m);
        await viewModel.SaveCommand.Execute();

        // Sugar doubles after both were saved: the versions' costs then stay, today's follow.
        await SetCostAsync(seeded.SugarId, 0.2m);
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedDetailsTab = ProductsViewModel.HistoryTab;
        await Settle.UntilAsync(viewModel, () => viewModel.VersionHistory.Rows.Count == 2, () => $"rows={viewModel.VersionHistory.Rows.Count}");

        var (latest, first) = (viewModel.VersionHistory.Rows[0], viewModel.VersionHistory.Rows[1]);
        first.BaseCostThen.Should().Be(10m, "100 g of sugar at 0.10, the whole batch one piece");
        first.BaseCostToday.Should().Be(20m, "the same 100 g at today's 0.20");
        latest.BaseCostThen.Should().Be(5m);
        latest.BaseCostToday.Should().Be(10m);

        // The chart: oldest first, at the costs then, with a gap for a version with no current price.
        viewModel.VersionHistory.IsTrendShown.Should().BeTrue();
        viewModel.VersionHistory.Trend.Select(p => (p.Label, p.BaseCost)).Should().Equal(("V1", 10d), ("V2", 5d));
        viewModel.VersionHistory.Trend.Should().OnlyContain(p => double.IsNaN(p.CurrentPrice));

        viewModel.VersionHistory.SelectedRow.Should().BeSameAs(latest, "the newest is picked first");
        viewModel.VersionHistory.Changes.Should().Equal("Topping: Sugar 100 g → 50 g");

        viewModel.VersionHistory.SelectedRow = first;
        viewModel.VersionHistory.CompareWith = VersionComparison.Current;
        viewModel.VersionHistory.Changes.Should().Equal("Topping: Sugar 100 g → 50 g");
    });

    [Fact]
    public Task A_version_keeps_the_wastage_it_was_priced_with_and_today_takes_the_wastage_now() => StaThread.RunAsync(async () =>
    {
        // Both versions are saved at the 3% wastage nothing in Settings gives.
        var (viewModel, _) = await OpenToppedProductAsync();
        Edit(viewModel, () => viewModel.UsageForm.FormLines.Single().Grams = 50m);
        await viewModel.SaveCommand.Execute();

        await SetWastageAsync(10m);
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedDetailsTab = ProductsViewModel.HistoryTab;
        await Settle.UntilAsync(viewModel, () => viewModel.VersionHistory.Rows.Count == 2, () => $"rows={viewModel.VersionHistory.Rows.Count}");

        var (latest, first) = (viewModel.VersionHistory.Rows[0], viewModel.VersionHistory.Rows[1]);
        first.BaseCostThen.Should().Be(10.3m, "100 g of sugar at 0.10, and the 3% wastage then");
        first.BaseCostToday.Should().Be(11m, "the same 100 g, and today's 10%");
        latest.BaseCostThen.Should().Be(5.15m);
        latest.BaseCostToday.Should().Be(5.5m);
        viewModel.VersionHistory.Changes.Should().Equal(["Topping: Sugar 100 g → 50 g"], "the wastage is the shop's, not a change of the product");
    });

    [Fact]
    public Task A_version_can_be_compared_with_any_other() => StaThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenToppedProductAsync();
        Edit(viewModel, () => viewModel.UsageForm.FormLines.Single().Grams = 50m);
        await viewModel.SaveCommand.Execute();
        Edit(viewModel, () => viewModel.EditingCurrentPrice = 25m);
        await viewModel.SaveCommand.Execute();
        await OpenHistoryAsync(viewModel, rows: 3);
        var history = viewModel.VersionHistory;
        var (third, first) = (history.Rows[0], history.Rows[2]);

        history.CompareChoices.Select(c => c.Label).Should().Equal(
            "The version before it", "The product as saved now", $"Version 3, {third.SavedText}", $"Version 2, {history.Rows[1].SavedText}", $"Version 1, {first.SavedText}");

        // Either way round, it reads from the older to the newer.
        history.SelectedRow = first;
        history.CompareWith = history.CompareChoices.Single(c => c.VersionId == third.Id);
        history.ChangesCaption.Should().Be("From version 1 to version 3:");
        history.Changes.Should().Equal("Current Price: set to 25.00", "Topping: Sugar 100 g → 50 g");
        history.SelectedRow = third;
        history.CompareWith = VersionComparison.With(first);
        history.ChangesCaption.Should().Be("From version 1 to version 3:");
        history.Changes.Should().Equal("Current Price: set to 25.00", "Topping: Sugar 100 g → 50 g");

        history.CompareWith = VersionComparison.With(third);
        history.Changes.Should().BeEmpty();
        history.ChangesCaption.Should().Be("Pick another version in Compare With to see what's different from version 3.");

        // A read again (a Save) keeps the choice; another product's versions go back to the version before it.
        await viewModel.LoadCommand.Execute();
        await Settle.UntilAsync(viewModel, () => history.Rows.Count == 3 && history.SelectedRow is not null, () => $"rows={history.Rows.Count}");
        history.CompareWith.Should().Be(VersionComparison.With(third));
        history.Clear();
        history.CompareWith.Should().Be(VersionComparison.Previous);
    });

    [Fact]
    public Task Restore_puts_a_version_into_the_form_as_unsaved_edits_and_Undo_takes_it_back() => StaThread.RunAsync(async () =>
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();
        var toppingId = viewModel.UsageEdits.Single().SubAssemblyId;
        Edit(viewModel, () => viewModel.UsageForm.FormLines.Single().Grams = 50m);
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
        viewModel.UsageForm.IsUsageFormOpen.Should().BeTrue("the section's tab shows its form, as it does when a product opens");
        viewModel.UsageForm.FormLines.Single().Grams.Should().Be(100m, "the form shows the restored lines, not an empty batch");
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
    });

    [Fact]
    public Task Print_on_the_History_tab_prints_the_version_picked_at_its_costs_then() => StaThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var (viewModel, seeded) = await OpenToppedProductAsync();
        Edit(viewModel, () => viewModel.UsageForm.FormLines.Single().Grams = 50m);
        await viewModel.SaveCommand.Execute();
        await SetCostAsync(seeded.SugarId, 0.2m);
        await viewModel.LoadCommand.Execute();
        await OpenHistoryAsync(viewModel, rows: 2);
        viewModel.VersionHistory.SelectedRow = viewModel.VersionHistory.Rows[1];

        var printout = viewModel.PreparePrintout().Value.Should().BeOfType<ProductPrintout>().Subject;

        printout.ProductName.Should().Be("Ube Roll (Version 1)");
        printout.SaveNote.Should().Be(ProductPrintout.VersionNote);
        printout.LastUpdatedText.Should().Be(viewModel.VersionHistory.Rows[1].SavedText);
        printout.Sections.Should().ContainSingle();
        printout.Costs.BaseCost.Should().Be(10m, "100 g of sugar at its cost then, 0.10 a gram");
        viewModel.UsageForm.FormLines.Single().Grams.Should().Be(50m, "printing a version leaves the form as it is");
    });

    [Fact]
    public Task Print_on_the_History_tab_with_no_version_picked_says_so() => StaThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenToppedProductAsync();
        await OpenHistoryAsync(viewModel, rows: 0);

        await viewModel.PrintCommand.Execute();

        viewModel.ErrorMessage.Should().Be("Pick a version in the History tab's table to print it.");
    });

    [Fact]
    public Task Restore_links_a_linked_sub_assembly_again_as_it_is_now() => StaThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var subAssemblies = new SubAssemblyRepository(CreateDbContextFactory());
        var doughId = await subAssemblies.SaveStandaloneAsync(
            new SubAssembly { Name = "House Dough", RoleId = doughRoleId }, [(flourId, 1000m)], Cancellation);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 50m)]));
        var viewModel = await OpenAsync(productId, vm => vm.UsageEdits.Count == 1);

        Edit(viewModel, () => viewModel.UsageForm.EditingUsageWeightPerPiece = 40m);
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
    });

    [Fact]
    public Task A_version_saved_from_another_tab_shows_when_History_is_shown_again() => StaThread.RunAsync(async () =>
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
    });

    [Fact]
    public Task Saving_lines_another_product_shares_records_a_version_of_that_product_too() => StaThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var (creamLoafId, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", doughRoleId, [new RecipeLine(flourId, 1000m)]);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        await SaveRecipeAsync(miniId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));
        var viewModel = await OpenAsync(miniId, vm => vm.UsageEdits.Count == 1);
        viewModel.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));

        await viewModel.EditUsageCommand.Execute(viewModel.UsageEdits.Single());
        Edit(viewModel, () => viewModel.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = butterId, Grams = 250m }));
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("A version was recorded for Cream Loaf.");
        var creamLoaf = await VersionsAsync(creamLoafId);
        creamLoaf.Select(v => v.Note).Should().Equal("Changed when Cream Loaf Mini was saved", ProductVersionRules.BaselineNote);
        ProductVersionContent.FromJson(creamLoaf[0].Content).Sections.Single().Lines.Should().HaveCount(2);
        (await VersionsAsync(miniId)).Should().HaveCount(2, "its own Save records it as ever: how it was, and how it is");
    });

    [Fact]
    public Task A_version_recorded_from_another_screen_shows_when_the_screen_is_shown_again() => StaThread.RunAsync(async () =>
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();
        Edit(viewModel, () => viewModel.EditingCurrentPrice = 25m);
        await viewModel.SaveCommand.Execute();
        await OpenHistoryAsync(viewModel, rows: 2);

        // As a Sub-Assemblies Save records one; showing the screen runs LoadCommand.
        await new ProductVersionRepository(CreateDbContextFactory()).AddAsync(
            seeded.ProductId,
            ProductVersionContent.FromJson((await VersionsAsync(seeded.ProductId))[0].Content) with { CurrentPrice = 30m },
            DateTime.Now,
            "From elsewhere",
            Cancellation);
        await viewModel.LoadCommand.Execute();

        await Settle.UntilAsync(viewModel,
            () => viewModel.VersionHistory.Rows.Count == 3,
            () => $"rows={viewModel.VersionHistory.Rows.Count}, error={viewModel.ErrorMessage}");
        viewModel.VersionHistory.Rows[0].Note.Should().Be("From elsewhere");
    });

    [Fact]
    public Task A_clone_or_a_variant_starts_its_history_with_where_it_came_from() => StaThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenToppedProductAsync();
        Edit(viewModel, () => viewModel.EditingCurrentPrice = 25m);
        await viewModel.SaveCommand.Execute();

        // The source as saved is its version 2.
        viewModel.EditingCloneName = "Ube Roll Copy";
        await viewModel.CloneCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        var cloneId = viewModel.SelectedProduct!.Id;
        var clone = (await VersionsAsync(cloneId)).Should().ContainSingle().Subject;
        clone.Note.Should().Be("Cloned from Ube Roll, version 2");
        ProductVersionContent.FromJson(clone.Content).CurrentPrice.Should().Be(25m);

        // A product with no versions has no number to give.
        var plainId = await AddProductAsync("Pan de Coco");
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == plainId);
        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady && viewModel.SelectedProduct?.Id == plainId, () => $"ready={viewModel.AreDetailsReady}");
        viewModel.EditingCloneName = "Pan de Coco Variant";
        await viewModel.CreateVariantCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        (await VersionsAsync(viewModel.SelectedProduct!.Id)).Single().Note.Should().Be("Variant of Pan de Coco");

        // The clone's first Save adds only what it changed: its history began when it was made.
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == cloneId);
        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady && viewModel.UsageForm.FormLines.Count == 1, () => $"lines={viewModel.UsageForm.FormLines.Count}");
        Edit(viewModel, () => viewModel.EditingCurrentPrice = 28m);
        await viewModel.SaveCommand.Execute();
        (await VersionsAsync(cloneId)).Select(v => v.Number).Should().Equal(2, 1);
    });

    [Fact]
    public Task Picking_another_product_while_History_shows_reads_its_versions_and_the_first_ones_again_on_the_way_back() => StaThread.RunAsync(async () =>
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();
        Edit(viewModel, () => viewModel.EditingCurrentPrice = 25m);
        await viewModel.SaveCommand.Execute();
        var otherId = await AddProductAsync("Pan de Sal");
        await viewModel.LoadCommand.Execute();
        await OpenHistoryAsync(viewModel, rows: 2);

        // The tab stays on History through both picks (found in the app 2026-10-17: coming back, it was empty).
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == otherId);
        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && viewModel.VersionHistory.ProductId == otherId,
            () => $"ready={viewModel.AreDetailsReady}, showing={viewModel.VersionHistory.ProductId}, error={viewModel.ErrorMessage}");
        viewModel.VersionHistory.IsEmpty.Should().BeTrue("Pan de Sal hasn't been saved since versions began to be kept");

        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == seeded.ProductId);
        await Settle.UntilAsync(viewModel,
            () => viewModel.VersionHistory.ProductId == seeded.ProductId && viewModel.VersionHistory.Rows.Count == 2,
            () => $"showing={viewModel.VersionHistory.ProductId}, rows={viewModel.VersionHistory.Rows.Count}, error={viewModel.ErrorMessage}");
        viewModel.SelectedDetailsTab.Should().Be(ProductsViewModel.HistoryTab);
    });

    [Fact]
    public Task Restoring_a_version_the_same_as_the_saved_product_leaves_nothing_to_save() => StaThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenToppedProductAsync();
        Edit(viewModel, () => viewModel.EditingCurrentPrice = 25m);
        await viewModel.SaveCommand.Execute();
        await OpenHistoryAsync(viewModel, rows: 2);
        viewModel.UsageForm.IsUsageFormOpen.Should().BeTrue("the section's form shows, as it does whenever a product opens");

        viewModel.VersionHistory.SelectedRow = viewModel.VersionHistory.Rows[0];
        await viewModel.RestoreVersionCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.History.IsDirty.Should().BeFalse("closing the section's form on the way isn't a change to save");
    });

    [Fact]
    public Task Restore_is_refused_while_an_ingredient_it_needs_is_gone() => StaThread.RunAsync(async () =>
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.3m);
        await viewModel.LoadCommand.Execute();
        Edit(viewModel, () => viewModel.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = butterId, Grams = 20m }));
        await viewModel.SaveCommand.Execute();
        Edit(viewModel, () => viewModel.UsageForm.FormLines.RemoveAt(1));
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
    });

    [Fact]
    public Task A_note_typed_into_a_version_is_saved() => StaThread.RunAsync(async () =>
    {
        var (viewModel, seeded) = await OpenToppedProductAsync();
        Edit(viewModel, () => viewModel.EditingCurrentPrice = 25m);
        await viewModel.SaveCommand.Execute();
        await OpenHistoryAsync(viewModel, rows: 2);

        viewModel.VersionHistory.Rows[0].Note = "  New price for October ";
        await Settle.UntilAsync(viewModel,
            () => viewModel.VersionHistory.Rows[0].Note == "New price for October",
            () => $"note={viewModel.VersionHistory.Rows[0].Note}, error={viewModel.ErrorMessage}");

        (await VersionsAsync(seeded.ProductId))[0].Note.Should().Be("New price for October");
    });

    [Fact]
    public Task A_Save_of_the_products_own_figures_reads_none_of_the_products_sharing_its_dough() => StaThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (pandesalId, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [new RecipeLine(flourId, 400m)]);
        for (var i = 1; i <= 3; i++)
        {
            var linkerId = await AddProductAsync($"Pandesal x {i}");
            await SaveRecipeAsync(linkerId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 60m)]));
        }

        var shared = await CommandsSavingPriceAsync(pandesalId);
        var alone = await CommandsSavingPriceAsync(ensaymadaId);

        shared.Should().Be(alone, "a new price changes no other product, so the three sharing Pandesal's dough aren't read");
    });

    /// <summary>
    /// The commands a Save of a new current price takes, with the product's History tab hidden: its own, and those of the
    /// reload's below-markup flags, which read every product in the background whatever the Save changed. Counted once
    /// they're all done, since that read is still going when the Save returns, and how far it had got varied the count
    /// (2026-10-30: 53 to 56, in a test run on its own).
    /// </summary>
    private async Task<int> CommandsSavingPriceAsync(int productId)
    {
        var viewModel = await OpenAsync(productId, vm => vm.UsageEdits.Count == 1);
        Edit(viewModel, () => viewModel.EditingCurrentPrice = 25m);
        int commands;
        using (var counter = SqlCommandCounter.Start())
        {
            await viewModel.SaveCommand.Execute();
            commands = await counter.SettledAsync();
        }

        viewModel.ErrorMessage.Should().BeNull();
        (await VersionsAsync(productId)).Should().HaveCount(2, "its first Save records how it was, then the new price");
        return commands;
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

        var viewModel = await OpenAsync(productId, vm => vm.UsageForm.FormLines.Count == 1);
        return (viewModel, new Seeded(productId, sugarId));
    }

    private static async Task OpenHistoryAsync(ProductsViewModel viewModel, int rows)
    {
        viewModel.SelectedDetailsTab = ProductsViewModel.HistoryTab;
        await Settle.UntilAsync(viewModel,
            () => viewModel.VersionHistory.Rows.Count == rows,
            () => $"rows={viewModel.VersionHistory.Rows.Count}, error={viewModel.ErrorMessage}");
    }

    private async Task<ProductsViewModel> OpenAsync(int productId, Func<ProductsViewModel, bool> ready)
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && ready(viewModel),
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.UsageEdits.Count}, lines={viewModel.UsageForm.FormLines.Count}");
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
