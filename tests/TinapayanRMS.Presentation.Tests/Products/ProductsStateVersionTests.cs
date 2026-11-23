using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// A product's version history on the Products screen: Save records a version when the recipe or price changes (and,
/// the first time, how the product stood before), the History tab shows each one priced then and today with what
/// changed in it, a note typed there is saved, and Restore puts a version into the form as unsaved edits Undo takes back.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class ProductsStateVersionTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task The_first_Save_records_how_the_product_was_and_how_it_is() => UiThread.RunAsync(async () =>
    {
        var (state, seeded) = await OpenToppedProductAsync();

        Edit(state, () => state.UsageForm.FormLines.Single().Grams = 50m);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        var versions = await VersionsAsync(seeded.ProductId);
        versions.Select(v => v.Number).Should().Equal(2, 1);
        versions[1].Note.Should().Be(ProductVersionRules.BaselineNote);
        GramsOf(versions[1]).Should().Be(100m, "the first is the product as it stood before this Save");
        GramsOf(versions[0]).Should().Be(50m);
    });

    [Test]
    public Task A_rename_alone_records_no_new_version() => UiThread.RunAsync(async () =>
    {
        var (state, seeded) = await OpenToppedProductAsync();

        Edit(state, () => state.EditingName = "Ube Roll Deluxe");
        await state.SaveCommand.ExecuteAsync();
        Edit(state, () => state.EditingName = "Ube Roll Supreme");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await VersionsAsync(seeded.ProductId)).Should().ContainSingle("only how it stood before the first Save: no recipe or price changed");
    });

    [Test]
    public Task A_price_change_records_a_version() => UiThread.RunAsync(async () =>
    {
        var (state, seeded) = await OpenToppedProductAsync();

        Edit(state, () => state.EditingCurrentPrice = 25m);
        await state.SaveCommand.ExecuteAsync();

        var latest = (await VersionsAsync(seeded.ProductId))[0];
        latest.Number.Should().Be(2);
        ProductVersionContent.FromJson(latest.Content).CurrentPrice.Should().Be(25m);
    });

    [Test]
    public Task The_History_tab_shows_each_version_then_and_today_and_what_changed() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var (state, seeded) = await OpenToppedProductAsync();
        Edit(state, () => state.UsageForm.FormLines.Single().Grams = 50m);
        await state.SaveCommand.ExecuteAsync();

        // Sugar doubles after both were saved: the versions' costs then stay, today's follow.
        await SetCostAsync(seeded.SugarId, 0.2m);
        await state.LoadCommand.ExecuteAsync();
        state.SelectedDetailsTab = ProductForm.HistoryTab;
        await Settle.UntilAsync(state, () => state.VersionHistory.Rows.Count == 2, () => $"rows={state.VersionHistory.Rows.Count}");

        var (latest, first) = (state.VersionHistory.Rows[0], state.VersionHistory.Rows[1]);
        first.BaseCostThen.Should().Be(10m, "100 g of sugar at 0.10, the whole batch one piece");
        first.BaseCostToday.Should().Be(20m, "the same 100 g at today's 0.20");
        latest.BaseCostThen.Should().Be(5m);
        latest.BaseCostToday.Should().Be(10m);

        // The chart: oldest first, at the costs then, with a gap for a version with no current price.
        state.VersionHistory.IsTrendShown.Should().BeTrue();
        state.VersionHistory.Trend.Select(p => (p.Label, p.BaseCost)).Should().Equal(("V1", 10d), ("V2", 5d));
        state.VersionHistory.Trend.Should().OnlyContain(p => double.IsNaN(p.CurrentPrice));

        state.VersionHistory.SelectedRow.Should().BeSameAs(latest, "the newest is picked first");
        state.VersionHistory.Changes.Should().Equal("Topping: Sugar 100 g → 50 g");

        state.VersionHistory.SelectedRow = first;
        state.VersionHistory.CompareWith = VersionComparison.Current;
        state.VersionHistory.Changes.Should().Equal("Topping: Sugar 100 g → 50 g");
    });

    [Test]
    public Task A_version_keeps_the_wastage_it_was_priced_with_and_today_takes_the_wastage_now() => UiThread.RunAsync(async () =>
    {
        // Both versions are saved at the 3% wastage nothing in Settings gives.
        var (state, _) = await OpenToppedProductAsync();
        Edit(state, () => state.UsageForm.FormLines.Single().Grams = 50m);
        await state.SaveCommand.ExecuteAsync();

        await SetWastageAsync(10m);
        await state.LoadCommand.ExecuteAsync();
        state.SelectedDetailsTab = ProductForm.HistoryTab;
        await Settle.UntilAsync(state, () => state.VersionHistory.Rows.Count == 2, () => $"rows={state.VersionHistory.Rows.Count}");

        var (latest, first) = (state.VersionHistory.Rows[0], state.VersionHistory.Rows[1]);
        first.BaseCostThen.Should().Be(10.3m, "100 g of sugar at 0.10, and the 3% wastage then");
        first.BaseCostToday.Should().Be(11m, "the same 100 g, and today's 10%");
        latest.BaseCostThen.Should().Be(5.15m);
        latest.BaseCostToday.Should().Be(5.5m);
        state.VersionHistory.Changes.Should().Equal(["Topping: Sugar 100 g → 50 g"], "the wastage is the shop's, not a change of the product");
    });

    [Test]
    public Task A_version_can_be_compared_with_any_other() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenToppedProductAsync();
        Edit(state, () => state.UsageForm.FormLines.Single().Grams = 50m);
        await state.SaveCommand.ExecuteAsync();
        Edit(state, () => state.EditingCurrentPrice = 25m);
        await state.SaveCommand.ExecuteAsync();
        await OpenHistoryAsync(state, rows: 3);
        var history = state.VersionHistory;
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
        await state.LoadCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => history.Rows.Count == 3 && history.SelectedRow is not null, () => $"rows={history.Rows.Count}");
        history.CompareWith.Should().Be(VersionComparison.With(third));
        history.Clear();
        history.CompareWith.Should().Be(VersionComparison.Previous);
    });

    [Test]
    public Task Restore_puts_a_version_into_the_form_as_unsaved_edits_and_Undo_takes_it_back() => UiThread.RunAsync(async () =>
    {
        var (state, seeded) = await OpenToppedProductAsync();
        var toppingId = state.Recipe.UsageEdits.Single().SubAssemblyId;
        Edit(state, () => state.UsageForm.FormLines.Single().Grams = 50m);
        Edit(state, () => state.EditingCurrentPrice = 25m);
        await state.SaveCommand.ExecuteAsync();
        await OpenHistoryAsync(state, rows: 2);

        state.VersionHistory.SelectedRow = state.VersionHistory.Rows[1];
        await state.VersionHistory.RestoreCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.SuccessMessage.Should().StartWith("Version 1 is in the form.");
        var topping = state.Recipe.UsageEdits.Single();
        topping.Lines.Single().Grams.Should().Be(100m);
        topping.SubAssemblyId.Should().Be(toppingId, "the product still owns it, so Save rewrites it in place");
        state.UsageForm.IsUsageFormOpen.Should().BeTrue("the section's tab shows its form, as it does when a product opens");
        state.UsageForm.FormLines.Single().Grams.Should().Be(100m, "the form shows the restored lines, not an empty batch");
        state.EditingCurrentPrice.Should().BeNull();
        state.History.IsDirty.Should().BeTrue("it's unsaved until Save");

        state.TryUndo().Should().BeTrue();
        state.Recipe.UsageEdits.Single().Lines.Single().Grams.Should().Be(50m);
        state.EditingCurrentPrice.Should().Be(25m);
        state.History.IsDirty.Should().BeFalse();

        state.TryRedo().Should().BeTrue();
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblyLines.SingleAsync(l => l.SubAssemblyId == toppingId, Cancellation)).Grams.Should().Be(100m);
        var versions = await VersionsAsync(seeded.ProductId);
        versions.Select(v => v.Number).Should().Equal(3, 2, 1);
        ProductVersionRules.IsSameRecipe(ProductVersionContent.FromJson(versions[0].Content), ProductVersionContent.FromJson(versions[2].Content))
            .Should().BeTrue("the restored version saved again is the first one's recipe and price");
    });

    [Test]
    public Task Print_on_the_History_tab_prints_the_version_picked_at_its_costs_then() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var (state, seeded) = await OpenToppedProductAsync();
        Edit(state, () => state.UsageForm.FormLines.Single().Grams = 50m);
        await state.SaveCommand.ExecuteAsync();
        await SetCostAsync(seeded.SugarId, 0.2m);
        await state.LoadCommand.ExecuteAsync();
        await OpenHistoryAsync(state, rows: 2);
        state.VersionHistory.SelectedRow = state.VersionHistory.Rows[1];

        var printout = state.PreparePrintout().Value.Should().BeOfType<ProductPrintout>().Subject;

        printout.ProductName.Should().Be("Ube Roll (Version 1)");
        printout.SaveNote.Should().Be(ProductPrintout.VersionNote);
        printout.LastUpdatedText.Should().Be(state.VersionHistory.Rows[1].SavedText);
        printout.Sections.Should().ContainSingle();
        printout.Costs.BaseCost.Should().Be(10m, "100 g of sugar at its cost then, 0.10 a gram");
        state.UsageForm.FormLines.Single().Grams.Should().Be(50m, "printing a version leaves the form as it is");
    });

    [Test]
    public Task Print_on_the_History_tab_with_no_version_picked_says_so() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenToppedProductAsync();
        await OpenHistoryAsync(state, rows: 0);

        await state.PrintCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("Pick a version in the History tab's table to print it.");
    });

    [Test]
    public Task Restore_links_a_linked_sub_assembly_again_as_it_is_now() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var subAssemblies = new SubAssemblyRepository(CreateDbContextFactory());
        var doughId = await subAssemblies.SaveStandaloneAsync(
            new SubAssembly { Name = "House Dough", RoleId = doughRoleId }, [(flourId, 1000m)], Cancellation);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 50m)]));
        var state = await OpenAsync(productId, vm => vm.Recipe.UsageEdits.Count == 1);

        Edit(state, () => state.UsageForm.TypeWeightPerPiece(40m));
        await state.SaveCommand.ExecuteAsync();
        await subAssemblies.SaveStandaloneAsync(
            new SubAssembly { Id = doughId, Name = "House Dough", RoleId = doughRoleId }, [(flourId, 800m)], Cancellation);
        await OpenHistoryAsync(state, rows: 2);

        state.VersionHistory.SelectedRow = state.VersionHistory.Rows[1];
        await state.VersionHistory.RestoreCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        var dough = state.Recipe.UsageEdits.Single();
        dough.SubAssemblyId.Should().Be(doughId);
        dough.IsLinked.Should().BeTrue();
        dough.AuthoredValue.Should().Be(50m, "the piece size is the product's own, and comes back");
        dough.Lines.Single().Grams.Should().Be(800m, "a version's lines are never written into a sub-assembly others may share");
        state.SuccessMessage.Should().Contain("House Dough has changed on Sub-Assemblies since");
    });

    [Test]
    public Task A_version_saved_from_another_tab_shows_when_History_is_shown_again() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenToppedProductAsync();
        Edit(state, () => state.EditingCurrentPrice = 25m);
        await state.SaveCommand.ExecuteAsync();
        await OpenHistoryAsync(state, rows: 2);

        state.SelectedDetailsTab = ProductForm.RecipeTab;
        Edit(state, () => state.EditingCurrentPrice = 30m);
        await state.SaveCommand.ExecuteAsync();
        await OpenHistoryAsync(state, rows: 3);

        state.VersionHistory.Rows[0].CurrentPrice.Should().Be(30m);
    });

    [Test]
    public Task Saving_lines_another_product_shares_records_a_version_of_that_product_too() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var (creamLoafId, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", doughRoleId, [new RecipeLine(flourId, 1000m)]);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        await SaveRecipeAsync(miniId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));
        var state = await OpenAsync(miniId, vm => vm.Recipe.UsageEdits.Count == 1);
        state.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));

        await state.Recipe.EditUsageCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());
        Edit(state, () => state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = butterId, Grams = 250m }));
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.SuccessMessage.Should().Be("A version was recorded for Cream Loaf.");
        var creamLoaf = await VersionsAsync(creamLoafId);
        creamLoaf.Select(v => v.Note).Should().Equal("Changed when Cream Loaf Mini was saved", ProductVersionRules.BaselineNote);
        ProductVersionContent.FromJson(creamLoaf[0].Content).Sections.Single().Lines.Should().HaveCount(2);
        (await VersionsAsync(miniId)).Should().HaveCount(2, "its own Save records it as ever: how it was, and how it is");
    });

    [Test]
    public Task A_version_recorded_from_another_screen_shows_when_the_screen_is_shown_again() => UiThread.RunAsync(async () =>
    {
        var (state, seeded) = await OpenToppedProductAsync();
        Edit(state, () => state.EditingCurrentPrice = 25m);
        await state.SaveCommand.ExecuteAsync();
        await OpenHistoryAsync(state, rows: 2);

        // As a Sub-Assemblies Save records one; showing the screen runs LoadCommand.
        await new ProductVersionRepository(CreateDbContextFactory()).AddAsync(
            seeded.ProductId,
            ProductVersionContent.FromJson((await VersionsAsync(seeded.ProductId))[0].Content) with { CurrentPrice = 30m },
            DateTime.Now,
            "From elsewhere",
            Cancellation);
        await state.LoadCommand.ExecuteAsync();

        await Settle.UntilAsync(state,
            () => state.VersionHistory.Rows.Count == 3,
            () => $"rows={state.VersionHistory.Rows.Count}, error={state.ErrorMessage}");
        state.VersionHistory.Rows[0].Note.Should().Be("From elsewhere");
    });

    [Test]
    public Task A_clone_or_a_variant_starts_its_history_with_where_it_came_from() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenToppedProductAsync();
        Edit(state, () => state.EditingCurrentPrice = 25m);
        await state.SaveCommand.ExecuteAsync();

        // The source as saved is its version 2. The clone opens in a tab of its own.
        state.EditingCloneName = "Ube Roll Copy";
        await state.CloneCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        var cloneForm = state.View.Form!;
        cloneForm.Should().NotBeSameAs(state);
        var cloneId = cloneForm.SelectedProduct!.Id;
        var clone = (await VersionsAsync(cloneId)).Should().ContainSingle().Subject;
        clone.Note.Should().Be("Cloned from Ube Roll, version 2");
        ProductVersionContent.FromJson(clone.Content).CurrentPrice.Should().Be(25m);

        // A product with no versions has no number to give.
        var plainId = await AddProductAsync("Pan de Coco");
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == plainId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.SelectedProduct?.Id == plainId, () => $"ready={state.AreDetailsReady}");
        state.EditingCloneName = "Pan de Coco Variant";
        await state.CreateVariantCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        (await VersionsAsync(state.View.Form!.SelectedProduct!.Id)).Single().Note.Should().Be("Variant of Pan de Coco");

        // The clone's first Save adds only what it changed: its history began when it was made.
        await Settle.UntilAsync(cloneForm, () => cloneForm.AreDetailsReady && cloneForm.UsageForm.FormLines.Count == 1, () => $"lines={cloneForm.UsageForm.FormLines.Count}");
        Edit(cloneForm, () => cloneForm.EditingCurrentPrice = 28m);
        await cloneForm.SaveCommand.ExecuteAsync();
        (await VersionsAsync(cloneId)).Select(v => v.Number).Should().Equal(2, 1);
    });

    [Test]
    public Task Picking_another_product_while_History_shows_reads_its_versions_and_the_first_ones_again_on_the_way_back() => UiThread.RunAsync(async () =>
    {
        var (state, seeded) = await OpenToppedProductAsync();
        Edit(state, () => state.EditingCurrentPrice = 25m);
        await state.SaveCommand.ExecuteAsync();
        var otherId = await AddProductAsync("Pan de Sal");
        await state.LoadCommand.ExecuteAsync();
        await OpenHistoryAsync(state, rows: 2);

        // The tab stays on History through both picks, and isn't empty coming back.
        state.Pick(state.Products.Single(p => p.Id == otherId));
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.VersionHistory.ProductId == otherId,
            () => $"ready={state.AreDetailsReady}, showing={state.VersionHistory.ProductId}, error={state.ErrorMessage}");
        state.VersionHistory.IsEmpty.Should().BeTrue("Pan de Sal hasn't been saved since versions began to be kept");

        state.Pick(state.Products.Single(p => p.Id == seeded.ProductId));
        await Settle.UntilAsync(state,
            () => state.VersionHistory.ProductId == seeded.ProductId && state.VersionHistory.Rows.Count == 2,
            () => $"showing={state.VersionHistory.ProductId}, rows={state.VersionHistory.Rows.Count}, error={state.ErrorMessage}");
        state.SelectedDetailsTab.Should().Be(ProductForm.HistoryTab);
    });

    [Test]
    public Task Restoring_a_version_the_same_as_the_saved_product_leaves_nothing_to_save() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenToppedProductAsync();
        Edit(state, () => state.EditingCurrentPrice = 25m);
        await state.SaveCommand.ExecuteAsync();
        await OpenHistoryAsync(state, rows: 2);
        state.UsageForm.IsUsageFormOpen.Should().BeTrue("the section's form shows, as it does whenever a product opens");

        state.VersionHistory.SelectedRow = state.VersionHistory.Rows[0];
        await state.VersionHistory.RestoreCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.History.IsDirty.Should().BeFalse("closing the section's form on the way isn't a change to save");
    });

    [Test]
    public Task Restoring_a_version_that_linked_a_section_the_product_owns_now_puts_it_back_as_its_own() => UiThread.RunAsync(async () =>
    {
        // Version 1 linked House Topping from Sub-Assemblies; its role was made product-owned since, so the product owns
        // it now. Restore linked it again, and the first Save was refused as one the product took after it was opened.
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 1);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.1m);
        var toppingId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "House Topping", RoleId = toppingRoleId }, [(sugarId, 100m)], Cancellation);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(toppingId, UsageAuthoredField.Yield, 1m)]));
        var state = await OpenAsync(productId, vm => vm.Recipe.UsageEdits.Count == 1);
        Edit(state, () => state.EditingCurrentPrice = 25m);
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();

        await new SubAssemblyRoleRepository(CreateDbContextFactory()).UpdateAsync(
            new SubAssemblyRole { Id = toppingRoleId, Name = "Topping", SortOrder = 1, IsProductOwned = true }, Cancellation);
        await state.LoadCommand.ExecuteAsync();
        await Settle.UntilAsync(state,
            () => state.Recipe.UsageEdits.Single().IsOwned,
            () => $"owned={state.Recipe.UsageEdits.Single().IsOwned}, error={state.ErrorMessage}");
        await OpenHistoryAsync(state, rows: 2);

        state.VersionHistory.SelectedRow = state.VersionHistory.Rows[1];
        await state.VersionHistory.RestoreCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        var section = state.Recipe.UsageEdits.Single();
        section.IsOwned.Should().BeTrue("the product owns it now, as a load turns it round");
        section.IsLinked.Should().BeFalse();
        section.SubAssemblyId.Should().Be(toppingId);
        section.Lines.Should().ContainSingle().Which.Grams.Should().Be(100m);
        state.SuccessMessage.Should().Contain("House Topping is this product's own now");
        state.EditingCurrentPrice.Should().NotBe(25m, "the version's price is back");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull("the first Save goes through");
        await using var dbContext = CreateDbContext();
        var usage = await dbContext.ProductSubAssemblyUsages.SingleAsync(u => u.ProductId == productId, Cancellation);
        usage.SubAssemblyId.Should().Be(toppingId);
        (await dbContext.SubAssemblies.SingleAsync(s => s.Id == toppingId, Cancellation)).OwnerProductId.Should().Be(productId);
    });

    [Test]
    public Task Restore_is_refused_while_an_ingredient_it_needs_is_gone() => UiThread.RunAsync(async () =>
    {
        var (state, seeded) = await OpenToppedProductAsync();
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.3m);
        await state.LoadCommand.ExecuteAsync();
        Edit(state, () => state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = butterId, Grams = 20m }));
        await state.SaveCommand.ExecuteAsync();
        Edit(state, () => state.UsageForm.FormLines.RemoveAt(1));
        await state.SaveCommand.ExecuteAsync();
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.RawMaterials.Where(m => m.Id == butterId).ExecuteDeleteAsync(Cancellation);
        }

        await state.LoadCommand.ExecuteAsync();
        await OpenHistoryAsync(state, rows: 3);
        state.VersionHistory.SelectedRow = state.VersionHistory.Rows[1];
        await state.VersionHistory.RestoreCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("Version 2 can't be restored: it needs Butter, which is no longer there.");
        state.Recipe.UsageEdits.Single().Lines.Should().ContainSingle("nothing changed");
        state.History.CanUndo.Should().BeFalse();
        _ = seeded;
    });

    [Test]
    public Task A_note_typed_into_a_version_is_saved() => UiThread.RunAsync(async () =>
    {
        var (state, seeded) = await OpenToppedProductAsync();
        Edit(state, () => state.EditingCurrentPrice = 25m);
        await state.SaveCommand.ExecuteAsync();
        await OpenHistoryAsync(state, rows: 2);

        state.VersionHistory.Rows[0].Note = "  New price for October ";
        await Settle.UntilAsync(state,
            () => state.VersionHistory.Rows[0].Note == "New price for October",
            () => $"note={state.VersionHistory.Rows[0].Note}, error={state.ErrorMessage}");

        (await VersionsAsync(seeded.ProductId))[0].Note.Should().Be("New price for October");
    });

    [Test]
    public Task A_Save_of_the_products_own_figures_reads_none_of_the_products_sharing_its_dough() => UiThread.RunAsync(async () =>
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
    /// they're all done, since that read is still going when the Save returns, and how far it had got would vary the
    /// count.
    /// </summary>
    private async Task<int> CommandsSavingPriceAsync(int productId)
    {
        var state = await OpenAsync(productId, vm => vm.Recipe.UsageEdits.Count == 1);
        Edit(state, () => state.EditingCurrentPrice = 25m);
        int commands;
        using (var counter = SqlCommandCounter.Start())
        {
            await state.SaveCommand.ExecuteAsync();
            commands = await counter.SettledAsync();
        }

        state.ErrorMessage.Should().BeNull();
        (await VersionsAsync(productId)).Should().HaveCount(2, "its first Save records how it was, then the new price");
        return commands;
    }

    private static void Edit(ProductForm state, Action change)
    {
        var recorder = (IUndoRecorder)state;
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
    private async Task<(ProductForm State, Seeded Seeded)> OpenToppedProductAsync()
    {
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 1, isProductOwned: true);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.1m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(toppingRoleId, [(sugarId, 100m)], UsageAuthoredField.Yield, 1m)], []));

        var state = await OpenAsync(productId, vm => vm.UsageForm.FormLines.Count == 1);
        return (state, new Seeded(productId, sugarId));
    }

    private static async Task OpenHistoryAsync(ProductForm state, int rows)
    {
        state.SelectedDetailsTab = ProductForm.HistoryTab;
        await Settle.UntilAsync(state,
            () => state.VersionHistory.Rows.Count == rows,
            () => $"rows={state.VersionHistory.Rows.Count}, error={state.ErrorMessage}");
    }

    private async Task<ProductForm> OpenAsync(int productId, Func<ProductForm, bool> ready)
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && ready(state),
            () => $"ready={state.AreDetailsReady}, usages={state.Recipe.UsageEdits.Count}, lines={state.UsageForm.FormLines.Count}");
        return state;
    }

    private ProductForm CreateState()
    {
        var factory = CreateDbContextFactory();

        return ProductsViews.Create(factory, CreateUnitOfWork()).Form!;
    }
}
