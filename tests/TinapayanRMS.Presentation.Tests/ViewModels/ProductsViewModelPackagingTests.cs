using System.Reactive.Linq;
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
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>
/// The product's packaging on the Products screen, on real repositories: the Packaging tab, which every product has,
/// with its table of packaging per piece, typed straight into, linked to a packaging set or copied from one or another
/// product (Link and Copy), and its bulk packaging. Make a Copy on a linked tab makes it the product's own, and Remove
/// Link takes it out. Add Sub-Assembly never offers packaging. Save writes the link and the lines together.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class ProductsViewModelPackagingTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
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

        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == bigId);
        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && viewModel.ErrorMessage is not null,
            () => $"ready={viewModel.AreDetailsReady}, error={viewModel.ErrorMessage}");

        viewModel.ErrorMessage.Should().Be(PackagingLinkRules.LinkedRoundInALoopText("Ensaymada"));
        viewModel.Packaging.LineEdits.Should().BeEmpty();
        viewModel.Costing.Costs.Packaging.Should().Be(0m);
    });

    [Test]
    public Task Packaging_per_piece_is_typed_straight_into_the_Packaging_tab_and_saved_as_the_products_own_lines() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Packaging", sortOrder: 6, isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.EditingName = "Ensaymada";
        viewModel.Recipe.SelectedRecipeTab.Should().BeSameAs(viewModel.Recipe.PackagingTab, "a new product has only the Packaging tab");
        viewModel.Packaging.IsPackagingEditable.Should().BeTrue("its table is there to type into from the start");

        viewModel.Packaging.LineEdits.Add(new PackagingLineRow { PackagingMaterialId = boxId, QtyPerUnit = 1m });
        await Settle.UntilAsync(viewModel, () => viewModel.Costing.Costs.Packaging == 30m, () => $"packaging={viewModel.Costing.Costs.Packaging}");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
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
        var viewModel = await CreateLoadedViewModelAsync();

        foreach (var mode in new[] { UsageFormMode.New, UsageFormMode.Link, UsageFormMode.Copy })
        {
            await viewModel.Recipe.AddUsageCommand.Execute(mode);
            viewModel.UsageForm.FormRoleOptions.Select(r => r.Name).Should().Equal("Dough");
            viewModel.UsageForm.LinkableSubAssemblies.Should().BeEmpty("neither the packaging set nor Cream Loaf's packaging is a recipe section");
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
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await Settle.UntilAsync(viewModel, () => viewModel.SelectedProduct?.Id == productId && viewModel.AreDetailsReady, () => $"selected={viewModel.SelectedProduct?.Id}, ready={viewModel.AreDetailsReady}");
        viewModel.Recipe.RecipeTabs.Should().Equal(viewModel.Recipe.PackagingTab);

        // Link offers only the Sub-Assemblies screen's packaging sets, never another product's packaging.
        await viewModel.Packaging.StartLinkingCommand.Execute();
        viewModel.Packaging.IsPickingSource.Should().BeTrue();
        viewModel.Packaging.SourceOptions.Select(o => o.PackagingSourceName).Should().Equal("Gift Box Set — packaging set");
        await PickAsync(viewModel, "Gift Box Set — packaging set");

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.Packaging.IsPickingSource.Should().BeFalse();
        viewModel.Packaging.IsPackagingLinked.Should().BeTrue();
        viewModel.Packaging.IsPackagingEditable.Should().BeFalse();
        viewModel.Packaging.PackagingLinkedToText.Should().Be("Linked to Gift Box Set (Sub-Assemblies)");
        viewModel.Costing.Costs.Packaging.Should().Be(31.5m, "one set per piece: a ₱30 box and 30 cm of ribbon at ₱0.05");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var link = await dbContext.PackagingLinks.SingleAsync(Cancellation);
            (link.ProductId, link.LinkedSubAssemblyId, link.LinkedProductId).Should().Be((productId, setId, null));
            (await dbContext.PackagingLines.CountAsync(Cancellation)).Should().Be(0, "a link has no lines of its own");
        }

        await Settle.UntilAsync(viewModel, () => viewModel.Packaging.LineEdits.Count == 2, () => $"lines={viewModel.Packaging.LineEdits.Count}");
        viewModel.Packaging.IsPackagingLinked.Should().BeTrue("the reload shows the saved link");

        // The set linked now isn't offered again.
        await viewModel.Packaging.StartLinkingCommand.Execute();
        viewModel.Packaging.IsPickingSource.Should().BeFalse();
        viewModel.ErrorMessage.Should().Be("This product links the only packaging set there is.");
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
        var viewModel = await OpenWithOwnPackagingAsync(bagId);

        // Copy offers the packaging sets, then every other product, never this one.
        await viewModel.Packaging.StartCopyingCommand.Execute();
        viewModel.Packaging.SourceOptions.Select(o => o.PackagingSourceName).Should().Equal("Gift Box Set — packaging set", "Cream Loaf — product");
        await PickAsync(viewModel, "Cream Loaf — product");

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.Packaging.IsPackagingLinked.Should().BeFalse();
        viewModel.Packaging.IsPackagingEditable.Should().BeTrue("a copy is this product's own to type into");
        viewModel.Packaging.LineEdits.Should().ContainSingle().Which.PackagingMaterialId.Should().Be(boxId);
        viewModel.Costing.Costs.Packaging.Should().Be(60m);

        // One step: undo puts the bag back.
        viewModel.TryUndo().Should().BeTrue();
        viewModel.Packaging.LineEdits.Should().ContainSingle().Which.PackagingMaterialId.Should().Be(bagId);
        viewModel.TryRedo().Should().BeTrue();

        viewModel.Packaging.LineEdits.Single().QtyPerUnit = 3m;
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
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
        var viewModel = await OpenWithOwnPackagingAsync(bagId);

        await viewModel.Packaging.StartCopyingCommand.Execute();
        viewModel.Packaging.PickedSource = viewModel.Packaging.SourceOptions.Single(o => o.PackagingSourceName == "Plain Roll — product");
        await Settle.UntilAsync(viewModel, () => viewModel.ErrorMessage is not null, () => $"error={viewModel.ErrorMessage}");

        viewModel.ErrorMessage.Should().Be("Plain Roll has no packaging for each piece to copy. Pick another.");
        viewModel.Packaging.IsPickingSource.Should().BeTrue();
        viewModel.Packaging.LineEdits.Should().ContainSingle().Which.PackagingMaterialId.Should().Be(bagId, "nothing changed");

        await viewModel.Packaging.CancelPickingCommand.Execute();
        viewModel.Packaging.IsPickingSource.Should().BeFalse();
        (await viewModel.SaveCommand.CanExecute.FirstAsync()).Should().BeFalse("opening and closing the picker changes nothing");
    });

    [Test]
    public Task Link_with_no_packaging_set_says_how_to_make_one() => UiThread.RunAsync(async () =>
    {
        var viewModel = await CreateLoadedViewModelAsync();

        await viewModel.Packaging.StartLinkingCommand.Execute();

        viewModel.Packaging.IsPickingSource.Should().BeFalse();
        viewModel.ErrorMessage.Should().StartWith("There's no packaging set to link yet.");
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
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await Settle.UntilAsync(viewModel, () => viewModel.Packaging.LineEdits.Count == 1, () => $"lines={viewModel.Packaging.LineEdits.Count}");
        viewModel.Recipe.RecipeTabs.Should().Equal(viewModel.Recipe.PackagingTab);
        viewModel.Packaging.IsPackagingLinked.Should().BeTrue();

        await viewModel.Packaging.MakeCopyCommand.Execute();

        viewModel.Packaging.IsPackagingLinked.Should().BeFalse();
        viewModel.Packaging.IsPackagingEditable.Should().BeTrue();
        viewModel.Packaging.LineEdits.Should().ContainSingle().Which.QtyPerUnit.Should().Be(1m);

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
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
        var viewModel = await OpenWithOwnPackagingAsync(boxId);
        viewModel.Packaging.CanMovePackaging.Should().BeTrue("the lines are the product's own");

        await viewModel.Packaging.MoveToSubAssembliesCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.Packaging.IsPackagingMoving.Should().BeTrue("the badge says Save moves it");
        viewModel.Packaging.CanMovePackaging.Should().BeFalse("it's on its way already");
        viewModel.Packaging.IsPackagingEditable.Should().BeTrue("its lines are still typed into here until Save");
        (await viewModel.SaveCommand.CanExecute.FirstAsync()).Should().BeTrue();

        // Typed after the press: the set takes it along.
        viewModel.Packaging.LineEdits.Single().QtyPerUnit = 2m;
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(viewModel, () => viewModel.Packaging.IsPackagingLinked && viewModel.Packaging.LineEdits.Count == 1, () => $"linked={viewModel.Packaging.IsPackagingLinked}");
        viewModel.Packaging.IsPackagingMoving.Should().BeFalse();
        viewModel.Packaging.IsPackagingLinkedToSet.Should().BeTrue();
        viewModel.Packaging.PackagingLinkedToText.Should().Be("Linked to Ensaymada Packaging (Sub-Assemblies)");
        viewModel.Packaging.IsPackagingEditable.Should().BeFalse("a linked set's lines are kept on the Sub-Assemblies screen");
        viewModel.Packaging.CanMovePackaging.Should().BeFalse();
        viewModel.Costing.Costs.Packaging.Should().Be(60m, "one set per piece, as the lines were: two ₱30 boxes");

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
        var viewModel = await OpenWithOwnPackagingAsync(boxId);

        await viewModel.Packaging.MoveToSubAssembliesCommand.Execute();
        (await viewModel.SaveCommand.CanExecute.FirstAsync()).Should().BeTrue();

        viewModel.TryUndo().Should().BeTrue();
        viewModel.Packaging.IsPackagingMoving.Should().BeFalse();
        viewModel.Packaging.CanMovePackaging.Should().BeTrue();
        (await viewModel.SaveCommand.CanExecute.FirstAsync()).Should().BeFalse();

        viewModel.TryRedo().Should().BeTrue();
        viewModel.Packaging.IsPackagingMoving.Should().BeTrue();
        (await viewModel.SaveCommand.CanExecute.FirstAsync()).Should().BeTrue();
    });

    [Test]
    public Task Move_to_Sub_Assemblies_with_no_packaging_lines_is_refused_with_a_message() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var viewModel = await OpenWithOwnPackagingAsync(boxId);
        viewModel.Packaging.SelectedLineEdit = viewModel.Packaging.LineEdits.Single();
        await viewModel.Packaging.RemoveLineCommand.Execute();

        await viewModel.Packaging.MoveToSubAssembliesCommand.Execute();

        viewModel.ErrorMessage.Should().Contain("at least one");
        viewModel.Packaging.IsPackagingMoving.Should().BeFalse();
        viewModel.Packaging.CanMovePackaging.Should().BeTrue();
    });

    [Test]
    public Task Remove_Link_takes_out_the_link_and_its_lines_and_keeps_the_bulk_packaging() => UiThread.RunAsync(async () =>
    {
        var (_, boxId) = await AddWrapperSetAndBoxAsync();
        var productId = await ProductIdAsync("Ensaymada");
        await SaveBulkAsync(productId, boxId, productsPerPack: 6);
        var viewModel = await OpenLinkedAsync();
        viewModel.Costing.Costs.Packaging.Should().Be(4m, "a ₱2 wrapper a piece, and a ₱12 box shared by 6");
        (await viewModel.Recipe.RemoveUsageCommand.CanExecute.FirstAsync()).Should().BeFalse("the Packaging tab isn't a sub-assembly to delete");
        (await viewModel.Packaging.RemoveLinkCommand.CanExecute.FirstAsync()).Should().BeTrue();

        await viewModel.Packaging.RemoveLinkCommand.Execute();

        viewModel.Recipe.SelectedRecipeTab.Should().BeSameAs(viewModel.Recipe.PackagingTab);
        viewModel.Packaging.IsPackagingLinked.Should().BeFalse();
        viewModel.Packaging.IsPackagingEditable.Should().BeTrue();
        viewModel.Packaging.LineEdits.Should().BeEmpty();
        viewModel.Packaging.BulkEdits.Should().ContainSingle();
        viewModel.Costing.Costs.Packaging.Should().Be(2m);
        (await viewModel.Packaging.RemoveLinkCommand.CanExecute.FirstAsync()).Should().BeFalse();

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.PackagingLinks.CountAsync(Cancellation)).Should().Be(0);
        (await dbContext.PackagingLines.Select(l => new { l.PackagingMaterialId, l.IsBulk }).ToListAsync(Cancellation))
            .Should().BeEquivalentTo([new { PackagingMaterialId = boxId, IsBulk = true }]);
    });

    [Test]
    public Task A_new_product_opens_on_the_Packaging_tab_and_can_save_bulk_packaging_alone() => UiThread.RunAsync(async () =>
    {
        var cartonId = await AddPackagingAsync("Carton", 12m);
        var viewModel = await CreateLoadedViewModelAsync();
        await viewModel.NewCommand.Execute();
        viewModel.Recipe.RecipeTabs.Should().Equal(viewModel.Recipe.PackagingTab);
        viewModel.Recipe.SelectedRecipeTab.Should().BeSameAs(viewModel.Recipe.PackagingTab);
        viewModel.Recipe.HasRecipeSections.Should().BeFalse();
        viewModel.Packaging.BulkCostText.Should().Be("No bulk packaging. Add what one bulk pack takes, such as a box several products share.");

        viewModel.EditingName = "Pandesal";
        viewModel.Packaging.BulkEdits.Add(new PackagingLineRow { PackagingMaterialId = cartonId, QtyPerUnit = 1m });
        viewModel.Packaging.BulkQuantity = 12;
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
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
        var viewModel = await OpenLinkedAsync();
        viewModel.Packaging.IsPackagingLinked.Should().BeTrue();
        viewModel.Packaging.BulkEdits.Should().BeEmpty();
        viewModel.Costing.Costs.Packaging.Should().Be(2m);

        // A ₱12 box for 6: typed as 1 box a pack, and 6 products in the box beside it. Its quantity stays as typed.
        viewModel.Packaging.BulkEdits.Add(new PackagingLineRow { PackagingMaterialId = boxId, QtyPerUnit = 1m });
        viewModel.Packaging.BulkQuantity = 6;

        await Settle.UntilAsync(viewModel, () => viewModel.Costing.Costs.Packaging == 4m, () => $"packaging={viewModel.Costing.Costs.Packaging}");
        viewModel.Packaging.BulkEdits.Single().QtyPerUnit.Should().Be(1m);
        viewModel.Packaging.BulkCostText.Should().Be("Pack cost 12.00 ÷ 6 products = 2.00 per unit");
        viewModel.Packaging.LinesCost.Should().Be(2m, "the Packaging per Piece heading's figure is its lines alone: the bulk share has its own caption");
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

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
        await reopened.Packaging.RemoveBulkLineCommand.Execute();
        reopened.Packaging.BulkEdits.Should().BeEmpty();
        reopened.Costing.Costs.Packaging.Should().Be(2m);
        reopened.TryUndo().Should().BeTrue();
        reopened.Packaging.BulkEdits.Should().ContainSingle();
    });

    [Test]
    public Task Bulk_lines_without_how_many_products_a_pack_holds_are_refused_at_Save() => UiThread.RunAsync(async () =>
    {
        var (_, boxId) = await AddWrapperSetAndBoxAsync();
        var viewModel = await OpenLinkedAsync();
        viewModel.Packaging.BulkEdits.Add(new PackagingLineRow { PackagingMaterialId = boxId, QtyPerUnit = 1m });
        await Settle.UntilAsync(viewModel, () => viewModel.Packaging.BulkPackCost == 12m, () => $"pack={viewModel.Packaging.BulkPackCost}");
        viewModel.Costing.Costs.Packaging.Should().Be(2m, "with no quantity the pack is shared among none yet");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().StartWith("Enter how many products one bulk pack holds");
        await using var dbContext = CreateDbContext();
        (await dbContext.PackagingLines.CountAsync(Cancellation)).Should().Be(0);
    });

    [Test]
    public Task Make_a_Copy_of_linked_packaging_keeps_the_bulk_packaging_apart() => UiThread.RunAsync(async () =>
    {
        var (wrapperId, boxId) = await AddWrapperSetAndBoxAsync();
        var viewModel = await OpenLinkedAsync();
        viewModel.Packaging.BulkEdits.Add(new PackagingLineRow { PackagingMaterialId = boxId, QtyPerUnit = 1m });
        viewModel.Packaging.BulkQuantity = 6;
        await Settle.UntilAsync(viewModel, () => viewModel.Costing.Costs.Packaging == 4m, () => $"packaging={viewModel.Costing.Costs.Packaging}");

        await viewModel.Packaging.MakeCopyCommand.Execute();

        viewModel.Packaging.IsPackagingLinked.Should().BeFalse();
        viewModel.Packaging.LineEdits.Select(l => l.PackagingMaterialId).Should().Equal(wrapperId);
        viewModel.Packaging.BulkEdits.Select(l => l.PackagingMaterialId).Should().Equal(boxId);
        viewModel.Costing.Costs.Packaging.Should().Be(4m);

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
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
    private async Task<ProductsViewModel> OpenLinkedAsync()
    {
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Name == "Ensaymada");
        await Settle.UntilAsync(viewModel,
            () => viewModel.Recipe.SelectedRecipeTab is PackagingRecipeTab && viewModel.Packaging.LineEdits.Count == 1, () => $"tab={viewModel.Recipe.SelectedRecipeTab?.Header}");
        return viewModel;
    }

    /// <summary>Ensaymada, with one <paramref name="boxId"/> a piece as its own packaging, open on its Packaging tab.</summary>
    private async Task<ProductsViewModel> OpenWithOwnPackagingAsync(int boxId)
    {
        var productId = await AddProductAsync("Ensaymada");
        await new PackagingLineRepository(CreateDbContextFactory())
            .SetLinesAsync(productId, [(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await Settle.UntilAsync(viewModel,
            () => viewModel.Recipe.SelectedRecipeTab is PackagingRecipeTab && viewModel.Packaging.LineEdits.Count == 1, () => $"tab={viewModel.Recipe.SelectedRecipeTab?.Header}");
        return viewModel;
    }

    private PackagingLinkRepository CreatePackagingLinkRepository() => new(CreateDbContextFactory());

    /// <summary>Picks <paramref name="name"/> in the Packaging tab's open Link or Copy picker, and waits for its lines to go in.</summary>
    private static async Task PickAsync(ProductsViewModel viewModel, string name)
    {
        viewModel.Packaging.PickedSource = viewModel.Packaging.SourceOptions.Single(o => o.PackagingSourceName == name);
        await Settle.UntilAsync(viewModel,
            () => !viewModel.Packaging.IsPickingSource || viewModel.ErrorMessage is not null,
            () => $"picking={viewModel.Packaging.IsPickingSource}, error={viewModel.ErrorMessage}");
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

    private async Task<ProductsViewModel> CreateLoadedViewModelAsync()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);

        var viewModel = new ProductsViewModel(
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
        await viewModel.LoadCommand.Execute();
        return viewModel;
    }
}
