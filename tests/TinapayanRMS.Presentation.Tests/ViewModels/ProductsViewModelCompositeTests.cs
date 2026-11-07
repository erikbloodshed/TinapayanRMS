using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>
/// A composite product on the Products screen, on real repositories: New composite product opens its Products table,
/// each line a product and how many of it go in, priced at the product's recipe cost; it has no Quantity Required and
/// adds only sections in product-owned roles and its packaging; Save writes the lines, undo takes them back, and a
/// refresh reprices them.
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class ProductsViewModelCompositeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    /// <summary>
    /// Ensaymada (a 1,000 g dough of flour at 0.05 a gram, 50 g a piece: 2.50 of recipe a piece) and Cheese Roll (the
    /// same dough at 100 g a piece: 5.00), each made of sub-assemblies, with the Dough and Packaging roles.
    /// </summary>
    private async Task<(int EnsaymadaId, int CheeseRollId, int DoughRoleId)> AddTwoProductsAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Packaging", sortOrder: 6, isPackaging: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m)], weightPerPiece: 50m);
        var (cheeseRollId, _) = await AddProductWithSubAssemblyAsync("Cheese Roll", doughRoleId, [(flourId, 1000m)], weightPerPiece: 100m);
        return (ensaymadaId, cheeseRollId, doughRoleId);
    }

    private Task<int> AddCompositeAsync(string name, params (int ProductId, int Quantity)[] components) =>
        new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = name, IsComposite = true },
            new RecipeDraft([], []) { Components = components },
            null,
            null,
            null,
            Cancellation);

    /// <summary>A line as the table's add row makes one, filled in as typing it would.</summary>
    private static ProductComponentRow Line(ProductsViewModel viewModel, int productId, int? quantity)
    {
        var row = viewModel.ComponentsTable.NewComponentRow();
        row.ComponentProductId = productId;
        row.Quantity = quantity;
        return row;
    }

    [Test]
    public Task A_new_composite_opens_on_its_Products_table_with_no_Quantity_Required_and_adds_only_product_owned_sections_and_packaging() => UiThread.RunAsync(async () =>
    {
        await AddTwoProductsAsync();
        await AddRoleAsync("Topping", sortOrder: 3, isProductOwned: true);
        var viewModel = await CreateLoadedViewModelAsync();

        await viewModel.NewCompositeCommand.Execute();

        viewModel.IsEditingComposite.Should().BeTrue();
        viewModel.IsEditingSingle.Should().BeFalse("Quantity Required's box shows only for a product of sub-assemblies");
        viewModel.HeaderTitle.Should().Be("New composite product");
        viewModel.CostUnitCaption.Should().Be("Per unit");
        viewModel.SuggestedSellingPriceLabel.Should().Be("Suggested Selling Price per Unit");
        viewModel.RecipeTabs.Should().Equal(viewModel.ComponentsTab);
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.ComponentsTab);
        viewModel.IsComponentsShowing.Should().BeTrue();
        viewModel.ComponentsTable.ComponentProductOptions.Select(p => p.Name).Should().Equal("Cheese Roll", "Ensaymada");
        (await CanSave(viewModel)).Should().BeFalse("nothing is typed into it yet");

        // Add Sub-Assembly opens the add form after the Products tab, offering the product-owned roles and packaging:
        // never Dough, and never the products' Dough sections to link or copy.
        await viewModel.AddUsageCommand.Execute(UsageFormMode.New);
        viewModel.RecipeTabs.Should().Equal(viewModel.ComponentsTab, viewModel.AddSubAssemblyTab);
        viewModel.AddSubAssemblyTab.Header.Should().Be("New Sub-Assembly");
        viewModel.UsageForm.FormRoleOptions.Select(r => r.Name).Should().Equal("Topping", "Packaging");
        viewModel.UsageForm.EditingUsageRole!.Name.Should().Be("Topping");
        viewModel.UsageForm.IsFormPackaging.Should().BeFalse();
        await viewModel.AddUsageCommand.Execute(UsageFormMode.Link);
        viewModel.UsageForm.LinkableSubAssemblies.Should().NotContain(o => !o.IsPackaging, "the products' sections are all Dough");
        await viewModel.AddUsageCommand.Execute(UsageFormMode.Copy);
        viewModel.UsageForm.LinkableSubAssemblies.Should().NotContain(o => !o.IsPackaging);

        // Back to a product of sub-assemblies: New takes the Products tab away.
        await viewModel.NewCommand.Execute();
        viewModel.IsEditingComposite.Should().BeFalse();
        viewModel.RecipeTabs.Should().BeEmpty();
        viewModel.HeaderTitle.Should().Be("New product");
        viewModel.AddSubAssemblyTab.Header.Should().Be("New Sub-Assembly");
    });

    [Test]
    public Task Products_typed_into_the_table_price_the_composite_at_their_recipe_cost_and_Save_writes_them() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        var viewModel = await CreateLoadedViewModelAsync();
        await viewModel.NewCompositeCommand.Execute();
        viewModel.EditingName = "Pasalubong Box";

        viewModel.ComponentsTable.ComponentEdits.Add(Line(viewModel, ensaymadaId, 6));
        viewModel.ComponentsTable.ComponentEdits.Add(Line(viewModel, cheeseRollId, 4));
        await Settle.UntilAsync(viewModel,
            () => viewModel.ComponentsTable.ComponentEdits.All(r => r.RecipeCost is not null),
            () => $"costs={string.Join(", ", viewModel.ComponentsTable.ComponentEdits.Select(r => r.RecipeCost))}");

        viewModel.ComponentsTable.ComponentEdits.Select(r => (r.Product!.Name, r.RecipeCost, r.Cost)).Should().Equal(
            ("Ensaymada", 2.50m, 15m), ("Cheese Roll", 5m, 20m));
        viewModel.Costing.Costs.IsComposite.Should().BeTrue();
        viewModel.Costing.Costs.PartsTitle.Should().Be("Products (ingredients)");
        viewModel.Costing.Costs.SubAssemblies.Should().Be(35m);
        viewModel.Costing.Costs.SubAssemblyLines.Select(l => (l.Name, l.Cost)).Should().Equal(("Ensaymada × 6", 15m), ("Cheese Roll × 4", 20m));
        viewModel.Costing.Costs.Material.Should().Be(35m, "it has no packaging yet, and a product's own stays out");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var box = await dbContext.Products.SingleAsync(p => p.Name == "Pasalubong Box", Cancellation);
            box.IsComposite.Should().BeTrue();
            box.QuantityRequired.Should().Be(1);
            (await dbContext.ProductComponents.Where(c => c.ProductId == box.Id).OrderBy(c => c.SortOrder)
                    .Select(c => new { c.ComponentProductId, c.Quantity }).ToListAsync(Cancellation))
                .Select(c => (c.ComponentProductId, c.Quantity)).Should().Equal((ensaymadaId, 6), (cheeseRollId, 4));
        }

        // Save reloads it as saved: the same lines, priced, on the tab it showed, with nothing left to save.
        viewModel.SelectedProduct!.IsComposite.Should().BeTrue();
        viewModel.ComponentsTable.ComponentEdits.Select(r => (r.ComponentProductId, r.Quantity, r.RecipeCost)).Should().Equal(
            (ensaymadaId, 6, 2.50m), (cheeseRollId, 4, 5m));
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.ComponentsTab);
        viewModel.Costing.Costs.SubAssemblies.Should().Be(35m);
        (await CanSave(viewModel)).Should().BeFalse();
    });

    [Test]
    public Task A_topping_over_the_whole_composite_adds_to_its_recipe_cost_on_a_tab_after_its_Products() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3, isProductOwned: true);
        var icingId = await AddIngredientAsync("Icing", costPerGram: 0.1m);
        var viewModel = await CreateLoadedViewModelAsync();
        await viewModel.NewCompositeCommand.Execute();
        viewModel.EditingName = "Party Box";
        viewModel.ComponentsTable.ComponentEdits.Add(Line(viewModel, ensaymadaId, 6));
        viewModel.ComponentsTable.ComponentEdits.Add(Line(viewModel, cheeseRollId, 4));
        await Settle.UntilAsync(viewModel,
            () => viewModel.ComponentsTable.ComponentEdits.All(r => r.RecipeCost is not null),
            () => $"costs={string.Join(", ", viewModel.ComponentsTable.ComponentEdits.Select(r => r.RecipeCost))}");

        // 500 g of icing at 0.10 a gram, 100 g of it on one box: 10.00.
        await viewModel.AddUsageCommand.Execute(UsageFormMode.New);
        viewModel.UsageForm.EditingUsageRole!.Id.Should().Be(toppingRoleId);
        viewModel.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = icingId, Grams = 500m });
        viewModel.UsageForm.EditingUsageWeightPerPiece = 100m;

        await viewModel.SaveCommand.Execute();
        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1 && viewModel.ComponentsTable.ComponentEdits.Count == 2,
            () => $"sections={viewModel.UsageEdits.Count} lines={viewModel.ComponentsTable.ComponentEdits.Count}");

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.RecipeTabs.Should().HaveCount(2);
        viewModel.RecipeTabs[0].Should().BeSameAs(viewModel.ComponentsTab);
        viewModel.RecipeTabs[1].Should().BeOfType<SubAssemblyRecipeTab>().Which.Header.Should().Be("Topping");
        viewModel.Costing.Costs.SubAssemblyLines.Select(l => (l.Name, l.Cost)).Should().Equal(
            ("Ensaymada × 6", 15m), ("Cheese Roll × 4", 20m), ("Topping", 10m));
        viewModel.Costing.Costs.SubAssemblies.Should().Be(45m);
        viewModel.ComponentsTable.ComponentsCost.Should().Be(35m, "the Products tab shows what its products bring");
        viewModel.Costing.Costs.PartsTitle.Should().Be("Products and sub-assemblies (ingredients)");
        viewModel.Costing.Costs.PartsBase.Should().Be("products and sub-assemblies");
        (await CanSave(viewModel)).Should().BeFalse();
        await using (var dbContext = CreateDbContext())
        {
            var box = await dbContext.Products.SingleAsync(p => p.Name == "Party Box", Cancellation);
            (await dbContext.SubAssemblies.SingleAsync(s => s.RoleId == toppingRoleId, Cancellation)).OwnerProductId.Should().Be(box.Id);
        }

        // Delete Sub-Assembly on its tab takes it out again, back to the Products tab.
        viewModel.SelectedRecipeTab = viewModel.RecipeTabs[1];
        await viewModel.RemoveUsageCommand.Execute();

        viewModel.UsageEdits.Should().BeEmpty();
        viewModel.RecipeTabs.Should().Equal(viewModel.ComponentsTab);
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.ComponentsTab);
        viewModel.Costing.Costs.SubAssemblies.Should().Be(35m);
        viewModel.Costing.Costs.PartsTitle.Should().Be("Products (ingredients)");
        (await CanSave(viewModel)).Should().BeTrue();
    });

    [Test]
    public Task Save_refuses_a_composite_of_one_product_or_with_a_line_half_filled_in() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        var viewModel = await CreateLoadedViewModelAsync();
        await viewModel.NewCompositeCommand.Execute();
        viewModel.EditingName = "Pasalubong Box";
        viewModel.ComponentsTable.ComponentEdits.Add(Line(viewModel, ensaymadaId, 6));

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("A composite product needs at least two different products.");

        viewModel.ComponentsTable.ComponentEdits.Add(Line(viewModel, cheeseRollId, null));
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("Enter how many Cheese Roll go in, or delete its line.");
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.AnyAsync(p => p.Name == "Pasalubong Box", Cancellation)).Should().BeFalse();
    });

    // A composite holding itself, through the products in it, can only come of an edit outside the app. Its read mustn't be
    // refused, taking every product's with it (the list's, Reports', the servers'): it opens and says so, and Save still
    // refuses the loop.
    [Test]
    public Task A_composite_that_holds_itself_opens_with_a_notice_and_Save_still_refuses_the_loop() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        var trayId = await AddCompositeAsync("Party Tray", (boxId, 1), (ensaymadaId, 12));
        await using (var dbContext = CreateDbContext())
        {
            dbContext.ProductComponents.Add(new ProductComponent { ProductId = boxId, ComponentProductId = trayId, Quantity = 1, SortOrder = 2 });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.Products.Select(p => p.Name).Should().Contain(["Pasalubong Box", "Party Tray"]);

        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == boxId);
        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady && viewModel.ComponentsTable.ComponentEdits.Count == 3, () => $"lines={viewModel.ComponentsTable.ComponentEdits.Count}");

        viewModel.ErrorMessage.Should().Be(CompositeProductRules.HoldsItselfText("Pasalubong Box"));
        viewModel.ComponentsTable.ComponentEdits[0].Quantity = 7;
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().Be(
            "Party Tray can't go into Pasalubong Box: it already contains Pasalubong Box, so Pasalubong Box would end up containing itself.");
    });

    [Test]
    public Task The_product_picker_leaves_out_the_composite_itself_and_every_composite_holding_it() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, doughRoleId) = await AddTwoProductsAsync();
        var cakeId = await AddProductAsync("Chocolate Cake");
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        await AddCompositeAsync("Party Package", (boxId, 2), (cakeId, 1));
        await AddCompositeAsync("Office Tray", (cakeId, 1), (ensaymadaId, 12));
        var viewModel = await CreateLoadedViewModelAsync();

        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == boxId);
        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady && viewModel.ComponentsTable.ComponentEdits.Count == 2, () => $"lines={viewModel.ComponentsTable.ComponentEdits.Count}");

        viewModel.ComponentsTable.ComponentProductOptions.Select(p => p.Name).Should().Equal(
            ["Cheese Roll", "Chocolate Cake", "Ensaymada", "Office Tray"],
            "the Party Package holds the box, and the box can't hold itself; the Office Tray doesn't hold it");

        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Name == "Party Package");
        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady && viewModel.ComponentsTable.ComponentEdits.Count == 2 && viewModel.ComponentsTable.ComponentEdits[0].ComponentProductId == boxId, () => $"lines={viewModel.ComponentsTable.ComponentEdits.Count}");
        viewModel.ComponentsTable.ComponentProductOptions.Select(p => p.Name).Should().Equal("Cheese Roll", "Chocolate Cake", "Ensaymada", "Office Tray", "Pasalubong Box");

        // A composite's own figures follow what's in it, the box inside it included.
        viewModel.Costing.Costs.SubAssemblyLines.Select(l => (l.Name, l.Cost)).Should().Equal(("Pasalubong Box × 2", 70m), ("Chocolate Cake × 1", 0m));

        // A product of sub-assemblies has no Products tab.
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == ensaymadaId);
        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1, () => $"sections={viewModel.UsageEdits.Count}");
        viewModel.IsEditingComposite.Should().BeFalse();
        viewModel.RecipeTabs.Should().NotContain(viewModel.ComponentsTab);
        viewModel.ComponentsTable.ComponentEdits.Should().BeEmpty();
        viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single().Usage.RoleId.Should().Be(doughRoleId);
    });

    [Test]
    public Task A_lines_edit_is_one_undo_step_and_Save_is_offered_only_while_the_lines_differ() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == boxId);
        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady && viewModel.ComponentsTable.ComponentEdits.Count == 2, () => $"lines={viewModel.ComponentsTable.ComponentEdits.Count}");
        (await CanSave(viewModel)).Should().BeFalse();

        // What the grid's undo behaviour does around a cell edit.
        var recorder = (IUndoRecorder)viewModel;
        recorder.BeginUndoStep();
        viewModel.ComponentsTable.ComponentEdits[0].Quantity = 8;
        recorder.EndUndoStep();

        viewModel.Costing.Costs.SubAssemblies.Should().Be(40m);
        (await CanSave(viewModel)).Should().BeTrue();

        await viewModel.UndoCommand.Execute();

        viewModel.ComponentsTable.ComponentEdits.Select(r => (r.ComponentProductId, r.Quantity)).Should().Equal((ensaymadaId, 6), (cheeseRollId, 4));
        viewModel.Costing.Costs.SubAssemblies.Should().Be(35m);
        (await CanSave(viewModel)).Should().BeFalse();

        await viewModel.RedoCommand.Execute();

        viewModel.ComponentsTable.ComponentEdits[0].Quantity.Should().Be(8);
        viewModel.Costing.Costs.SubAssemblies.Should().Be(40m);

        // Remove Line is a step of its own too.
        viewModel.ComponentsTable.SelectedComponentEdit = viewModel.ComponentsTable.ComponentEdits[1];
        await viewModel.RemoveComponentCommand.Execute();
        viewModel.ComponentsTable.ComponentEdits.Should().ContainSingle();
        await viewModel.UndoCommand.Execute();
        viewModel.ComponentsTable.ComponentEdits.Should().HaveCount(2);
    });

    [Test]
    public Task Including_each_products_packaging_adds_it_to_the_composites_packaging_as_one_undo_step_and_Save_keeps_it() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        await AddOwnPackagingAsync(ensaymadaId, "Ensaymada Bag", 1.50m);
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == boxId);
        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady && viewModel.ComponentsTable.ComponentEdits.Count == 2, () => $"lines={viewModel.ComponentsTable.ComponentEdits.Count}");

        viewModel.EditingIncludesComponentPackaging.Should().BeFalse();
        viewModel.Costing.Costs.Packaging.Should().Be(0m, "a product's own packaging stays out until the box is ticked");
        viewModel.Costing.Costs.PackagingTitle.Should().Be("Packaging");
        viewModel.ComponentsCaption.Should().Contain("Its own packaging stays out");
        viewModel.ComponentsTable.ComponentEdits.Select(r => (r.PackagingCost, r.Cost)).Should().Equal((1.50m, 15m), (0m, 20m));
        viewModel.ComponentsTable.ComponentsCost.Should().Be(35m);

        // What ticking the box does.
        viewModel.IsComponentPackagingTicked = true;

        // The Products table: each line's Cost is its recipe and its packaging, and the figure above adds them up.
        viewModel.ComponentsTable.ComponentEdits.Select(r => (r.RecipeCost, r.PackagingCost, r.Cost)).Should().Equal((2.50m, 1.50m, 24m), (5m, 0m, 20m));
        viewModel.ComponentsTable.ComponentsCost.Should().Be(44m);
        PrintedComponent.From(viewModel.ComponentsTable.ComponentEdits).Should().Equal(
            new PrintedComponent("Ensaymada", 6, 2.50m, 24m, 1.50m), new PrintedComponent("Cheese Roll", 4, 5m, 20m, 0m));

        // The costing still keeps them apart: the products' lines are their recipe, and their packaging is packaging.
        viewModel.Costing.Costs.SubAssemblyLines.Select(l => (l.Name, l.Cost)).Should().Equal(("Ensaymada × 6", 15m), ("Cheese Roll × 4", 20m));

        viewModel.EditingIncludesComponentPackaging.Should().BeTrue();
        viewModel.Costing.Costs.ComponentPackaging.Should().Be(9m, "6 bags of 1.50; Cheese Roll has none");
        viewModel.Costing.Costs.Packaging.Should().Be(9m);
        viewModel.Costing.Costs.OwnPackaging.Should().Be(0m, "the Packaging tab lists the composite's own lines only");
        viewModel.Costing.Costs.PackagingTitle.Should().Be("Packaging (own and products')");
        viewModel.Costing.Costs.SubAssemblies.Should().Be(35m, "the recipe cost is unchanged");
        viewModel.Costing.Costs.Material.Should().Be(44m);
        viewModel.ComponentsCaption.Should().Contain("and its own packaging");
        (await CanSave(viewModel)).Should().BeTrue();

        await viewModel.UndoCommand.Execute();

        viewModel.EditingIncludesComponentPackaging.Should().BeFalse();
        viewModel.Costing.Costs.Packaging.Should().Be(0m);
        viewModel.ComponentsTable.ComponentEdits.Select(r => r.Cost).Should().Equal(15m, 20m);
        viewModel.ComponentsTable.ComponentsCost.Should().Be(35m);
        (await CanSave(viewModel)).Should().BeFalse();

        await viewModel.RedoCommand.Execute();
        viewModel.Costing.Costs.Packaging.Should().Be(9m);

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.Products.SingleAsync(p => p.Id == boxId, Cancellation)).IncludesComponentPackaging.Should().BeTrue();
        }

        viewModel.EditingIncludesComponentPackaging.Should().BeTrue();
        viewModel.Costing.Costs.Packaging.Should().Be(9m);
        // The rows reloaded after Save follow the box too.
        viewModel.ComponentsTable.ComponentEdits.Select(r => r.Cost).Should().Equal(24m, 20m);
        (await CanSave(viewModel)).Should().BeFalse();

        // A new composite starts with its products' packaging left out.
        await viewModel.NewCompositeCommand.Execute();
        viewModel.EditingIncludesComponentPackaging.Should().BeFalse();
    });

    /// <summary>A packaging material of one piece, costing <paramref name="cost"/>, as <paramref name="productId"/>'s own packaging line.</summary>
    private async Task AddOwnPackagingAsync(int productId, string name, decimal cost)
    {
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var categoryId = await AddPackagingCategoryAsync($"{name} Boxes");
        int materialId;
        await using (var dbContext = CreateDbContext())
        {
            var material = new RawMaterial
            {
                Type = RawMaterialType.Packaging, Name = name, CategoryId = categoryId, BaseUnitId = pieceId,
                CostPerBaseUnit = cost, BufferedCostPerBaseUnit = cost,
            };
            dbContext.RawMaterials.Add(material);
            await dbContext.SaveChangesAsync(Cancellation);
            materialId = material.Id;
        }

        await new PackagingLineRepository(CreateDbContextFactory())
            .SetLinesAsync(productId, [(materialId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);
    }

    [Test]
    public Task A_refresh_reprices_the_lines_as_their_products_now_are_without_counting_as_an_edit() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, doughRoleId) = await AddTwoProductsAsync();
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == boxId);
        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady && viewModel.ComponentsTable.ComponentEdits.Count == 2, () => $"lines={viewModel.ComponentsTable.ComponentEdits.Count}");

        // Ensaymada's pieces are made twice the size elsewhere: each now takes 5.00 of recipe.
        var doughId = await SubAssemblyIdAsync(ensaymadaId, doughRoleId);
        await SaveRecipeAsync(ensaymadaId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 100m)]));

        await viewModel.LoadCommand.Execute();

        viewModel.ComponentsTable.ComponentEdits[0].RecipeCost.Should().Be(5m);
        viewModel.Costing.Costs.SubAssemblies.Should().Be(50m);
        (await CanSave(viewModel)).Should().BeFalse("a product repriced elsewhere isn't this product's change");
    });

    [Test]
    public Task A_product_a_composite_holds_isnt_deleted_and_the_message_names_the_composite() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == ensaymadaId);
        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1, () => $"sections={viewModel.UsageEdits.Count}");

        await viewModel.DeleteCommand.Execute();

        viewModel.ErrorMessage.Should().Be("Pasalubong Box holds this product. Take it out there first.");
        viewModel.Products.Should().Contain(p => p.Id == ensaymadaId);
    });

    [Test]
    public Task A_composites_printout_has_its_products_table_and_its_costing_per_unit() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == boxId);
        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady && viewModel.ComponentsTable.ComponentEdits.Count == 2, () => $"lines={viewModel.ComponentsTable.ComponentEdits.Count}");

        var printout = viewModel.PreparePrintout().Value.Should().BeOfType<ProductPrintout>().Subject;

        printout.IsComposite.Should().BeTrue();
        printout.Sections.Should().BeEmpty();
        printout.CostUnitCaption.Should().Be("Per unit");
        printout.Components.Should().Equal(
            new PrintedComponent("Ensaymada", 6, 2.50m, 15m),
            new PrintedComponent("Cheese Roll", 4, 5m, 20m));
        printout.Costs.SubAssemblies.Should().Be(35m);
    });

    [Test]
    public Task The_lists_menus_offer_a_new_composite_product_where_they_offer_a_new_product() => UiThread.RunAsync(async () =>
    {
        await AddTwoProductsAsync();
        var viewModel = await CreateLoadedViewModelAsync();
        var production = viewModel.ProductTree.StageFolder(ProductStage.Production);

        viewModel.TreeMenuFor(null).Select(i => i.Header).Should().StartWith(["New Product", "New Composite Product"]);
        var here = viewModel.TreeMenuFor(production).Single(i => i.Header == "New Composite Product Here");

        await ((ReactiveCommand<ProductFolderNode?, RxVoid>)here.Command!).Execute((ProductFolderNode?)here.Parameter);

        viewModel.IsEditingComposite.Should().BeTrue();
        viewModel.NewProductStage.Should().Be(ProductStage.Production);
        viewModel.NewProductTag.Should().Be("Goes in Production");
    });

    /// <summary>Whether the Save button would be enabled: the command's own CanExecute, as the View binds it.</summary>
    private static async Task<bool> CanSave(ProductsViewModel viewModel) =>
        await viewModel.SaveCommand.CanExecute.FirstAsync();

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
            new IngredientBomLineRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new ProductCategoryRepository(factory),
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
