using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// The product's packaging on the Products screen, on real repositories: the Packaging tab, which every product has,
/// with its table of packaging per piece, typed straight into, linked to a packaging set or copied from one or another
/// product (Link and Copy), and its bulk packaging. Make a Copy on a linked tab makes it the product's own, and Remove
/// Link takes it out. Add Sub-Assembly never offers packaging. Save writes the link and the lines together.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class ProductsStatePackagingTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    // A product whose packaging links come round in a loop, as only an edit outside the app can leave them, mustn't fail
    // every product's read with it. It opens, showing no packaging, and says so.
    [Test]
    public Task A_product_whose_packaging_links_come_round_in_a_loop_opens_with_a_notice() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Packaging", sortOrder: 6, isPackaging: true);
        var bigId = await AddProductAsync("Ensaymada");
        var miniId = await AddProductAsync("Ensaymada Mini");
        await using (var dbContext = CreateDbContext())
        {
            dbContext.PackagingLinks.Add(new PackagingLink { ProductId = bigId, LinkedProductId = miniId });
            dbContext.PackagingLinks.Add(new PackagingLink { ProductId = miniId, LinkedProductId = bigId });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var state = await CreateLoadedStateAsync();
        state.SelectedProduct = state.Products.Single(p => p.Id == bigId);
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.ErrorMessage is not null,
            () => $"ready={state.AreDetailsReady}, error={state.ErrorMessage}");

        state.ErrorMessage.Should().Be(PackagingLinkRules.LinkedRoundInALoopText("Ensaymada"));
        state.Packaging.LineEdits.Should().BeEmpty();
        state.Costing.Costs.Packaging.Should().Be(0m);
    });

    [Test]
    public Task Packaging_per_piece_is_typed_straight_into_the_Packaging_tab_and_saved_as_the_products_own_lines() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Packaging", sortOrder: 6, isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var state = await CreateLoadedStateAsync();
        state.EditingName = "Ensaymada";
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.PackagingTab, "a new product has only the Packaging tab");
        state.Packaging.IsPackagingEditable.Should().BeTrue("its table is there to type into from the start");

        state.Packaging.LineEdits.Add(new PackagingLineRow { PackagingMaterialId = boxId, QtyPerUnit = 1m });
        await Settle.UntilAsync(state, () => state.Costing.Costs.Packaging == 30m, () => $"packaging={state.Costing.Costs.Packaging}");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var line = await dbContext.PackagingLines.SingleAsync(Cancellation);
        (line.PackagingMaterialId, line.QtyPerUnit, line.IsBulk).Should().Be((boxId, 1m, false));
        (await dbContext.PackagingLinks.CountAsync(Cancellation)).Should().Be(0);
    });

    [Test]
    public Task Add_Sub_Assembly_never_offers_packaging_which_the_Packaging_tab_takes() => UiThread.RunAsync(async () =>
    {
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 6, isPackaging: true);
        await AddRoleAsync("Dough", sortOrder: 1);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Gift Box Set", RoleId = packagingRoleId }, [(boxId, null, 1m)], Cancellation);
        var otherId = await AddProductAsync("Cream Loaf");
        await new PackagingLineRepository(CreateDbContextFactory())
            .SetLinesAsync(otherId, [(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);
        var state = await CreateLoadedStateAsync();

        foreach (var mode in new[] { UsageFormMode.New, UsageFormMode.Link, UsageFormMode.Copy })
        {
            await state.Recipe.AddUsageCommand.ExecuteAsync(mode);
            state.UsageForm.FormRoleOptions.Select(r => r.Name).Should().Equal("Dough");
            state.UsageForm.LinkableSubAssemblies.Should().BeEmpty("neither the packaging set nor Cream Loaf's packaging is a recipe section");
        }
    });

    [Test]
    public Task Link_on_the_Packaging_tab_shows_a_sets_lines_read_only_and_Save_writes_the_link() => UiThread.RunAsync(async () =>
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var ribbonId = await AddPackagingAsync("Ribbon", 0.05m, "cm");
        var setId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Gift Box Set", RoleId = packagingRoleId }, [(boxId, null, 1m), (ribbonId, null, 30m)], Cancellation);
        var productId = await AddProductAsync("Ensaymada");
        await AddProductAsync("Cream Loaf");
        var state = await CreateLoadedStateAsync();
        state.SelectedProduct = state.Products.Single(p => p.Id == productId);
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == productId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}, ready={state.AreDetailsReady}");
        state.Recipe.RecipeTabs.Should().Equal(state.Recipe.PackagingTab);

        // Link offers only the Sub-Assemblies screen's packaging sets, never another product's packaging.
        await state.Packaging.StartLinkingCommand.ExecuteAsync();
        state.Packaging.IsPickingSource.Should().BeTrue();
        state.Packaging.SourceOptions.Select(o => o.PackagingSourceName).Should().Equal("Gift Box Set — packaging set");
        await PickAsync(state, "Gift Box Set — packaging set");

        state.ErrorMessage.Should().BeNull();
        state.Packaging.IsPickingSource.Should().BeFalse();
        state.Packaging.IsPackagingLinked.Should().BeTrue();
        state.Packaging.IsPackagingEditable.Should().BeFalse();
        state.Packaging.PackagingLinkedToText.Should().Be("Linked to Gift Box Set (Sub-Assemblies)");
        state.Costing.Costs.Packaging.Should().Be(31.5m, "one set per piece: a ₱30 box and 30 cm of ribbon at ₱0.05");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var link = await dbContext.PackagingLinks.SingleAsync(Cancellation);
            (link.ProductId, link.LinkedSubAssemblyId, link.LinkedProductId).Should().Be((productId, setId, null));
            (await dbContext.PackagingLines.CountAsync(Cancellation)).Should().Be(0, "a link has no lines of its own");
        }

        await Settle.UntilAsync(state, () => state.Packaging.LineEdits.Count == 2, () => $"lines={state.Packaging.LineEdits.Count}");
        state.Packaging.IsPackagingLinked.Should().BeTrue("the reload shows the saved link");

        // The set linked now isn't offered again.
        await state.Packaging.StartLinkingCommand.ExecuteAsync();
        state.Packaging.IsPickingSource.Should().BeFalse();
        state.ErrorMessage.Should().Be("This product links the only packaging set there is.");
    });

    [Test]
    public Task Copy_on_the_Packaging_tab_puts_another_products_lines_in_place_of_its_own_as_one_undo_step() => UiThread.RunAsync(async () =>
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var bagId = await AddPackagingAsync("Bag", 1m);
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Gift Box Set", RoleId = packagingRoleId }, [(boxId, null, 1m)], Cancellation);
        var sourceId = await AddProductAsync("Cream Loaf");
        await new PackagingLineRepository(CreateDbContextFactory())
            .SetLinesAsync(sourceId, [(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 2m)], Cancellation);
        var state = await OpenWithOwnPackagingAsync(bagId);

        // Copy offers the packaging sets, then every other product, never this one.
        await state.Packaging.StartCopyingCommand.ExecuteAsync();
        state.Packaging.SourceOptions.Select(o => o.PackagingSourceName).Should().Equal("Gift Box Set — packaging set", "Cream Loaf — product");
        await PickAsync(state, "Cream Loaf — product");

        state.ErrorMessage.Should().BeNull();
        state.Packaging.IsPackagingLinked.Should().BeFalse();
        state.Packaging.IsPackagingEditable.Should().BeTrue("a copy is this product's own to type into");
        state.Packaging.LineEdits.Should().ContainSingle().Which.PackagingMaterialId.Should().Be(boxId);
        state.Costing.Costs.Packaging.Should().Be(60m);

        // One step: undo puts the bag back.
        state.TryUndo().Should().BeTrue();
        state.Packaging.LineEdits.Should().ContainSingle().Which.PackagingMaterialId.Should().Be(bagId);
        state.TryRedo().Should().BeTrue();

        state.Packaging.LineEdits.Single().QtyPerUnit = 3m;
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var productId = await dbContext.Products.Where(p => p.Name == "Ensaymada").Select(p => p.Id).SingleAsync(Cancellation);
        (await dbContext.PackagingLines.SingleAsync(l => l.ProductId == productId, Cancellation)).QtyPerUnit.Should().Be(3m);
        (await dbContext.PackagingLines.SingleAsync(l => l.ProductId == sourceId, Cancellation)).QtyPerUnit.Should().Be(2m, "the source is untouched");
        (await dbContext.PackagingLinks.CountAsync(Cancellation)).Should().Be(0);
    });

    [Test]
    public Task Picking_packaging_with_no_lines_is_refused_and_the_picker_stays_open() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Packaging", isPackaging: true);
        var bagId = await AddPackagingAsync("Bag", 1m);
        await AddProductAsync("Plain Roll");
        var state = await OpenWithOwnPackagingAsync(bagId);

        await state.Packaging.StartCopyingCommand.ExecuteAsync();
        state.Packaging.PickedSource = state.Packaging.SourceOptions.Single(o => o.PackagingSourceName == "Plain Roll — product");
        await Settle.UntilAsync(state, () => state.ErrorMessage is not null, () => $"error={state.ErrorMessage}");

        state.ErrorMessage.Should().Be("Plain Roll has no packaging for each piece to copy. Pick another.");
        state.Packaging.IsPickingSource.Should().BeTrue();
        state.Packaging.LineEdits.Should().ContainSingle().Which.PackagingMaterialId.Should().Be(bagId, "nothing changed");

        await state.Packaging.CancelPickingCommand.ExecuteAsync();
        state.Packaging.IsPickingSource.Should().BeFalse();
        state.SaveCommand.CanExecute().Should().BeFalse("opening and closing the picker changes nothing");
    });

    [Test]
    public Task Link_with_no_packaging_set_says_how_to_make_one() => UiThread.RunAsync(async () =>
    {
        var state = await CreateLoadedStateAsync();

        await state.Packaging.StartLinkingCommand.ExecuteAsync();

        state.Packaging.IsPickingSource.Should().BeFalse();
        state.ErrorMessage.Should().StartWith("There's no packaging set to link yet.");
    });

    [Test]
    public Task Make_a_Copy_on_a_linked_Packaging_tab_unlinks_it_on_Save_and_keeps_its_lines() => UiThread.RunAsync(async () =>
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var setId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Gift Box Set", RoleId = packagingRoleId }, [(boxId, null, 1m)], Cancellation);
        var productId = await AddProductAsync("Ensaymada");
        await CreatePackagingLinkRepository().SetSubAssemblyLinkAsync(productId, setId, Cancellation);
        var state = await CreateLoadedStateAsync();
        state.SelectedProduct = state.Products.Single();
        await Settle.UntilAsync(state, () => state.Packaging.LineEdits.Count == 1, () => $"lines={state.Packaging.LineEdits.Count}");
        state.Recipe.RecipeTabs.Should().Equal(state.Recipe.PackagingTab);
        state.Packaging.IsPackagingLinked.Should().BeTrue();

        await state.Packaging.MakeCopyCommand.ExecuteAsync();

        state.Packaging.IsPackagingLinked.Should().BeFalse();
        state.Packaging.IsPackagingEditable.Should().BeTrue();
        state.Packaging.LineEdits.Should().ContainSingle().Which.QtyPerUnit.Should().Be(1m);

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.PackagingLinks.CountAsync(Cancellation)).Should().Be(0);
        var line = await dbContext.PackagingLines.SingleAsync(Cancellation);
        (line.ProductId, line.PackagingMaterialId, line.QtyPerUnit).Should().Be((productId, boxId, 1m),
            "the lines the link showed are written even though they match what was loaded");
    });

    [Test]
    public Task Move_to_Sub_Assemblies_on_the_Packaging_tab_saves_its_lines_as_a_set_and_links_it() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var state = await OpenWithOwnPackagingAsync(boxId);
        state.Packaging.CanMovePackaging.Should().BeTrue("the lines are the product's own");

        await state.Packaging.MoveToSubAssembliesCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.Packaging.IsPackagingMoving.Should().BeTrue("the badge says Save moves it");
        state.Packaging.CanMovePackaging.Should().BeFalse("it's on its way already");
        state.Packaging.IsPackagingEditable.Should().BeTrue("its lines are still typed into here until Save");
        state.SaveCommand.CanExecute().Should().BeTrue();

        // Typed after the press: the set takes it along.
        state.Packaging.LineEdits.Single().QtyPerUnit = 2m;
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state, () => state.Packaging.IsPackagingLinked && state.Packaging.LineEdits.Count == 1, () => $"linked={state.Packaging.IsPackagingLinked}");
        state.Packaging.IsPackagingMoving.Should().BeFalse();
        state.Packaging.IsPackagingLinkedToSet.Should().BeTrue();
        state.Packaging.PackagingLinkedToText.Should().Be("Linked to Ensaymada Packaging (Sub-Assemblies)");
        state.Packaging.IsPackagingEditable.Should().BeFalse("a linked set's lines are kept on the Sub-Assemblies screen");
        state.Packaging.CanMovePackaging.Should().BeFalse();
        state.Costing.Costs.Packaging.Should().Be(60m, "one set per piece, as the lines were: two ₱30 boxes");

        await using var dbContext = CreateDbContext();
        var set = await dbContext.SubAssemblies.SingleAsync(Cancellation);
        (set.Name, set.OwnerProductId).Should().Be(("Ensaymada Packaging", (int?)null));
        (await dbContext.SubAssemblyPackagingLines.SingleAsync(Cancellation)).QtyPerSet.Should().Be(2m);
        (await dbContext.PackagingLinks.SingleAsync(Cancellation)).LinkedSubAssemblyId.Should().Be(set.Id);
        (await dbContext.PackagingLines.CountAsync(Cancellation)).Should().Be(0);
    });

    [Test]
    public Task Undoing_Move_to_Sub_Assemblies_on_the_Packaging_tab_keeps_its_lines_the_products_own_and_stops_offering_Save() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var state = await OpenWithOwnPackagingAsync(boxId);

        await state.Packaging.MoveToSubAssembliesCommand.ExecuteAsync();
        state.SaveCommand.CanExecute().Should().BeTrue();

        state.TryUndo().Should().BeTrue();
        state.Packaging.IsPackagingMoving.Should().BeFalse();
        state.Packaging.CanMovePackaging.Should().BeTrue();
        state.SaveCommand.CanExecute().Should().BeFalse();

        state.TryRedo().Should().BeTrue();
        state.Packaging.IsPackagingMoving.Should().BeTrue();
        state.SaveCommand.CanExecute().Should().BeTrue();
    });

    [Test]
    public Task Move_to_Sub_Assemblies_with_no_packaging_lines_is_refused_with_a_message() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var state = await OpenWithOwnPackagingAsync(boxId);
        state.Packaging.SelectedLineEdit = state.Packaging.LineEdits.Single();
        await state.Packaging.RemoveLineCommand.ExecuteAsync();

        await state.Packaging.MoveToSubAssembliesCommand.ExecuteAsync();

        state.ErrorMessage.Should().Contain("at least one");
        state.Packaging.IsPackagingMoving.Should().BeFalse();
        state.Packaging.CanMovePackaging.Should().BeTrue();
    });

    [Test]
    public Task Remove_Link_takes_out_the_link_and_its_lines_and_keeps_the_bulk_packaging() => UiThread.RunAsync(async () =>
    {
        var (_, boxId) = await AddWrapperSetAndBoxAsync();
        var productId = await ProductIdAsync("Ensaymada");
        await SaveBulkAsync(productId, boxId, productsPerPack: 6);
        var state = await OpenLinkedAsync();
        state.Costing.Costs.Packaging.Should().Be(4m, "a ₱2 wrapper a piece, and a ₱12 box shared by 6");
        state.Recipe.RemoveUsageCommand.CanExecute().Should().BeFalse("the Packaging tab isn't a sub-assembly to delete");
        state.Packaging.RemoveLinkCommand.CanExecute().Should().BeTrue();

        await state.Packaging.RemoveLinkCommand.ExecuteAsync();

        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.PackagingTab);
        state.Packaging.IsPackagingLinked.Should().BeFalse();
        state.Packaging.IsPackagingEditable.Should().BeTrue();
        state.Packaging.LineEdits.Should().BeEmpty();
        state.Packaging.BulkEdits.Should().ContainSingle();
        state.Costing.Costs.Packaging.Should().Be(2m);
        state.Packaging.RemoveLinkCommand.CanExecute().Should().BeFalse();

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.PackagingLinks.CountAsync(Cancellation)).Should().Be(0);
        (await dbContext.PackagingLines.Select(l => new { l.PackagingMaterialId, l.IsBulk }).ToListAsync(Cancellation))
            .Should().BeEquivalentTo([new { PackagingMaterialId = boxId, IsBulk = true }]);
    });

    [Test]
    public Task A_new_product_opens_on_the_Packaging_tab_and_can_save_bulk_packaging_alone() => UiThread.RunAsync(async () =>
    {
        var cartonId = await AddPackagingAsync("Carton", 12m);
        var state = await CreateLoadedStateAsync();
        await state.NewCommand.ExecuteAsync();
        state.Recipe.RecipeTabs.Should().Equal(state.Recipe.PackagingTab);
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.PackagingTab);
        state.Recipe.HasRecipeSections.Should().BeFalse();
        state.Packaging.BulkCostText.Should().Be("No bulk packaging. Add what one bulk pack takes, such as a box several products share.");

        state.EditingName = "Pandesal";
        state.Packaging.BulkEdits.Add(new PackagingLineRow { PackagingMaterialId = cartonId, QtyPerUnit = 1m });
        state.Packaging.BulkQuantity = 12;
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var product = await dbContext.Products.SingleAsync(Cancellation);
        (product.Name, product.BulkPackagingQuantity).Should().Be(("Pandesal", (int?)12));
        var line = await dbContext.PackagingLines.SingleAsync(Cancellation);
        (line.PackagingMaterialId, line.IsBulk).Should().Be((cartonId, true));
    });

    [Test]
    public Task Bulk_packaging_costs_each_product_its_share_of_the_pack_saves_and_reads_back() => UiThread.RunAsync(async () =>
    {
        var (wrapperId, boxId) = await AddWrapperSetAndBoxAsync();
        var state = await OpenLinkedAsync();
        state.Packaging.IsPackagingLinked.Should().BeTrue();
        state.Packaging.BulkEdits.Should().BeEmpty();
        state.Costing.Costs.Packaging.Should().Be(2m);

        // A ₱12 box for 6: typed as 1 box a pack, and 6 products in the box beside it. Its quantity stays as typed.
        state.Packaging.BulkEdits.Add(new PackagingLineRow { PackagingMaterialId = boxId, QtyPerUnit = 1m });
        state.Packaging.BulkQuantity = 6;

        await Settle.UntilAsync(state, () => state.Costing.Costs.Packaging == 4m, () => $"packaging={state.Costing.Costs.Packaging}");
        state.Packaging.BulkEdits.Single().QtyPerUnit.Should().Be(1m);
        state.Packaging.BulkCostText.Should().Be("Pack cost 12.00 ÷ 6 products = 2.00 per unit");
        state.Packaging.LinesCost.Should().Be(2m, "the Packaging per Piece heading's figure is its lines alone: the bulk share has its own caption");
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();

        await using (var dbContext = CreateDbContext())
        {
            var line = await dbContext.PackagingLines.SingleAsync(Cancellation);
            (line.PackagingMaterialId, line.IsBulk, line.QtyPerUnit).Should().Be((boxId, true, 1m));
            (await dbContext.Products.SingleAsync(Cancellation)).BulkPackagingQuantity.Should().Be(6);
            (await dbContext.PackagingLinks.CountAsync(Cancellation)).Should().Be(1, "the set stays linked");
        }

        var reopened = await OpenLinkedAsync();
        reopened.Packaging.LineEdits.Select(l => l.PackagingMaterialId).Should().Equal(wrapperId);
        reopened.Packaging.BulkEdits.Should().ContainSingle().Which.QtyPerUnit.Should().Be(1m);
        reopened.Packaging.BulkQuantity.Should().Be(6);
        reopened.Costing.Costs.Packaging.Should().Be(4m);

        // Remove Line is one undo step.
        reopened.Packaging.SelectedBulkEdit = reopened.Packaging.BulkEdits.Single();
        await reopened.Packaging.RemoveBulkLineCommand.ExecuteAsync();
        reopened.Packaging.BulkEdits.Should().BeEmpty();
        reopened.Costing.Costs.Packaging.Should().Be(2m);
        reopened.TryUndo().Should().BeTrue();
        reopened.Packaging.BulkEdits.Should().ContainSingle();
    });

    [Test]
    public Task Bulk_lines_without_how_many_products_a_pack_holds_are_refused_at_Save() => UiThread.RunAsync(async () =>
    {
        var (_, boxId) = await AddWrapperSetAndBoxAsync();
        var state = await OpenLinkedAsync();
        state.Packaging.BulkEdits.Add(new PackagingLineRow { PackagingMaterialId = boxId, QtyPerUnit = 1m });
        await Settle.UntilAsync(state, () => state.Packaging.BulkPackCost == 12m, () => $"pack={state.Packaging.BulkPackCost}");
        state.Costing.Costs.Packaging.Should().Be(2m, "with no quantity the pack is shared among none yet");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().StartWith("Enter how many products one bulk pack holds");
        await using var dbContext = CreateDbContext();
        (await dbContext.PackagingLines.CountAsync(Cancellation)).Should().Be(0);
    });

    [Test]
    public Task Make_a_Copy_of_linked_packaging_keeps_the_bulk_packaging_apart() => UiThread.RunAsync(async () =>
    {
        var (wrapperId, boxId) = await AddWrapperSetAndBoxAsync();
        var state = await OpenLinkedAsync();
        state.Packaging.BulkEdits.Add(new PackagingLineRow { PackagingMaterialId = boxId, QtyPerUnit = 1m });
        state.Packaging.BulkQuantity = 6;
        await Settle.UntilAsync(state, () => state.Costing.Costs.Packaging == 4m, () => $"packaging={state.Costing.Costs.Packaging}");

        await state.Packaging.MakeCopyCommand.ExecuteAsync();

        state.Packaging.IsPackagingLinked.Should().BeFalse();
        state.Packaging.LineEdits.Select(l => l.PackagingMaterialId).Should().Equal(wrapperId);
        state.Packaging.BulkEdits.Select(l => l.PackagingMaterialId).Should().Equal(boxId);
        state.Costing.Costs.Packaging.Should().Be(4m);

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.PackagingLinks.CountAsync(Cancellation)).Should().Be(0);
        (await dbContext.PackagingLines.OrderBy(l => l.IsBulk).Select(l => new { l.PackagingMaterialId, l.IsBulk }).ToListAsync(Cancellation))
            .Should().BeEquivalentTo([new { PackagingMaterialId = wrapperId, IsBulk = false }, new { PackagingMaterialId = boxId, IsBulk = true }]);
    });

    /// <summary>A Wrapper Set (one ₱2 wrapper a unit), Ensaymada linking it, and a ₱12 box; returns the wrapper's and the box's ids.</summary>
    private async Task<(int WrapperId, int BoxId)> AddWrapperSetAndBoxAsync()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var wrapperId = await AddPackagingAsync("Wrapper", 2m);
        var boxId = await AddPackagingAsync("Box", 12m);
        var setId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Wrapper Set", RoleId = packagingRoleId }, [(wrapperId, null, 1m)], Cancellation);
        var productId = await AddProductAsync("Ensaymada");
        await CreatePackagingLinkRepository().SetSubAssemblyLinkAsync(productId, setId, Cancellation);
        return (wrapperId, boxId);
    }

    /// <summary>A new screen open on Ensaymada's Packaging tab, its linked lines in.</summary>
    private async Task<ProductsState> OpenLinkedAsync()
    {
        var state = await CreateLoadedStateAsync();
        state.SelectedProduct = state.Products.Single(p => p.Name == "Ensaymada");
        await Settle.UntilAsync(state,
            () => state.Recipe.SelectedRecipeTab is PackagingRecipeTab && state.Packaging.LineEdits.Count == 1, () => $"tab={state.Recipe.SelectedRecipeTab?.Header}");
        return state;
    }

    /// <summary>Ensaymada, with one <paramref name="boxId"/> a piece as its own packaging, open on its Packaging tab.</summary>
    private async Task<ProductsState> OpenWithOwnPackagingAsync(int boxId)
    {
        var productId = await AddProductAsync("Ensaymada");
        await new PackagingLineRepository(CreateDbContextFactory())
            .SetLinesAsync(productId, [(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);
        var state = await CreateLoadedStateAsync();
        state.SelectedProduct = state.Products.Single(p => p.Id == productId);
        await Settle.UntilAsync(state,
            () => state.Recipe.SelectedRecipeTab is PackagingRecipeTab && state.Packaging.LineEdits.Count == 1, () => $"tab={state.Recipe.SelectedRecipeTab?.Header}");
        return state;
    }

    private PackagingLinkRepository CreatePackagingLinkRepository() => new(CreateDbContextFactory());

    /// <summary>Picks <paramref name="name"/> in the Packaging tab's open Link or Copy picker, and waits for its lines to go in.</summary>
    private static async Task PickAsync(ProductsState state, string name)
    {
        state.Packaging.PickedSource = state.Packaging.SourceOptions.Single(o => o.PackagingSourceName == name);
        await Settle.UntilAsync(state,
            () => !state.Packaging.IsPickingSource || state.ErrorMessage is not null,
            () => $"picking={state.Packaging.IsPickingSource}, error={state.ErrorMessage}");
    }

    private async Task<int> ProductIdAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.Products.Where(p => p.Name == name).Select(p => p.Id).SingleAsync(Cancellation);
    }

    /// <summary>Gives the product bulk packaging: one <paramref name="materialId"/> a pack, shared by <paramref name="productsPerPack"/>.</summary>
    private async Task SaveBulkAsync(int productId, int materialId, int productsPerPack)
    {
        await using var dbContext = CreateDbContext();
        var (qtyPerUnit, unitsPerPackagingUnit) = ProductRules.ReconcileReciprocal(PackagingLineAuthoredField.QtyPerUnit, 1m);
        dbContext.PackagingLines.Add(new PackagingLine
        {
            ProductId = productId,
            PackagingMaterialId = materialId,
            QtyPerUnit = qtyPerUnit,
            UnitsPerPackagingUnit = unitsPerPackagingUnit,
            AuthoredField = PackagingLineAuthoredField.QtyPerUnit,
            IsBulk = true,
        });
        (await dbContext.Products.SingleAsync(p => p.Id == productId, Cancellation)).BulkPackagingQuantity = productsPerPack;
        await dbContext.SaveChangesAsync(Cancellation);
    }

    /// <summary>A packaging material with <paramref name="baseUnit"/> as its base unit, costing <paramref name="cost"/> a base unit, buffered the same.</summary>
    private async Task<int> AddPackagingAsync(string name, decimal cost, string baseUnit = "pc")
    {
        var unitId = await GetOrCreateUnitOfMeasureAsync(baseUnit, baseUnit);

        await using var dbContext = CreateDbContext();
        var category = await dbContext.RawMaterialCategories.FirstOrDefaultAsync(c => c.Name == "Boxes", Cancellation)
            ?? new RawMaterialCategory { Name = "Boxes" };
        var material = new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = name,
            Category = category,
            BaseUnitId = unitId,
            CostPerBaseUnit = cost,
            BufferedCostPerBaseUnit = cost,
        };
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
    }

    private async Task<ProductsState> CreateLoadedStateAsync()
    {
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);

        var state = new ProductsState(
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
            new RawMaterialCategoryRepository(factory),
            new ProductCategoryRepository(factory),
            new ProductFolderRepository(factory),
            new ProductVersionRepository(factory),
            new ProductValidator(),
            new CostingParametersValidator(),
            new ProductCategoryValidator(),
            new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
        await state.LoadCommand.ExecuteAsync();
        return state;
    }
}
