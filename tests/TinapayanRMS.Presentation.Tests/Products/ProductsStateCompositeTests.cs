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
/// A composite product on the Products screen, on real repositories: New composite product opens its Products table,
/// each line a product and how many of it go in, priced at the product's recipe cost; it has no Quantity Required and
/// adds only sections in product-owned roles and its packaging; Save writes the lines, undo takes them back, and a
/// refresh reprices them.
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class ProductsStateCompositeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
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
    private static ProductComponentRow Line(ProductForm state, int productId, int? quantity)
    {
        var row = state.ComponentsTable.NewComponentRow();
        row.ComponentProductId = productId;
        row.Quantity = quantity;
        return row;
    }

    [Test]
    public Task A_new_composite_opens_on_its_Products_table_with_no_Quantity_Required_and_adds_only_product_owned_sections() => UiThread.RunAsync(async () =>
    {
        await AddTwoProductsAsync();
        await AddRoleAsync("Topping", sortOrder: 3, isProductOwned: true);
        var state = await CreateLoadedStateAsync();

        await state.NewCompositeCommand.ExecuteAsync();

        state.IsEditingComposite.Should().BeTrue();
        state.IsEditingSingle.Should().BeFalse("Quantity Required's box shows only for a product of sub-assemblies");
        state.HeaderTitle.Should().Be("New composite product");
        state.Costing.CostUnitCaption.Should().Be("Per unit");
        state.Costing.SuggestedSellingPriceLabel.Should().Be("Suggested Selling Price per Unit");
        state.Recipe.RecipeTabs.Should().Equal(state.Recipe.ComponentsTab, state.Recipe.PackagingTab);
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.ComponentsTab);
        state.Recipe.IsComponentsShowing.Should().BeTrue();
        state.ComponentsTable.ComponentProductOptions.Select(p => p.Name).Should().Equal("Cheese Roll", "Ensaymada");
        CanSave(state).Should().BeFalse("nothing is typed into it yet");

        // Add Sub-Assembly opens the add form after the Products tab, offering the product-owned roles: never Dough, never
        // the products' Dough sections to link or copy, and never packaging, which is the Packaging tab's.
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.Recipe.RecipeTabs.Should().Equal(state.Recipe.ComponentsTab, state.Recipe.AddSubAssemblyTab, state.Recipe.PackagingTab);
        state.Recipe.AddSubAssemblyTab.Header.Should().Be("New Sub-Assembly");
        state.UsageForm.FormRoleOptions.Select(r => r.Name).Should().Equal("Topping");
        state.UsageForm.EditingUsageRole!.Name.Should().Be("Topping");
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Link);
        state.UsageForm.LinkableSubAssemblies.Should().BeEmpty("the products' sections are all Dough");
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Copy);
        state.UsageForm.LinkableSubAssemblies.Should().BeEmpty();

        // Back to a product of sub-assemblies: New takes the Products tab away.
        await state.NewCommand.ExecuteAsync();
        state.IsEditingComposite.Should().BeFalse();
        state.Recipe.RecipeTabs.Should().Equal(state.Recipe.PackagingTab);
        state.HeaderTitle.Should().Be("New product");
        state.Recipe.AddSubAssemblyTab.Header.Should().Be("New Sub-Assembly");
    });

    [Test]
    public Task Products_typed_into_the_table_price_the_composite_at_their_recipe_cost_and_Save_writes_them() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        var state = await CreateLoadedStateAsync();
        await state.NewCompositeCommand.ExecuteAsync();
        state.EditingName = "Pasalubong Box";

        state.ComponentsTable.ComponentEdits.Add(Line(state, ensaymadaId, 6));
        state.ComponentsTable.ComponentEdits.Add(Line(state, cheeseRollId, 4));
        await Settle.UntilAsync(state,
            () => state.ComponentsTable.ComponentEdits.All(r => r.RecipeCost is not null),
            () => $"costs={string.Join(", ", state.ComponentsTable.ComponentEdits.Select(r => r.RecipeCost))}");

        state.ComponentsTable.ComponentEdits.Select(r => (r.Product!.Name, r.RecipeCost, r.Cost)).Should().Equal(
            ("Ensaymada", 2.50m, 15m), ("Cheese Roll", 5m, 20m));
        state.Costing.Costs.IsComposite.Should().BeTrue();
        state.Costing.Costs.PartsTitle.Should().Be("Products (ingredients)");
        state.Costing.Costs.SubAssemblies.Should().Be(35m);
        state.Costing.Costs.SubAssemblyLines.Select(l => (l.Name, l.Cost)).Should().Equal(("Ensaymada × 6", 15m), ("Cheese Roll × 4", 20m));
        state.Costing.Costs.Material.Should().Be(35m, "it has no packaging yet, and a product's own stays out");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
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
        state.SelectedProduct!.IsComposite.Should().BeTrue();
        state.ComponentsTable.ComponentEdits.Select(r => (r.ComponentProductId, r.Quantity, r.RecipeCost)).Should().Equal(
            (ensaymadaId, 6, 2.50m), (cheeseRollId, 4, 5m));
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.ComponentsTab);
        state.Costing.Costs.SubAssemblies.Should().Be(35m);
        CanSave(state).Should().BeFalse();
    });

    [Test]
    public Task A_topping_over_the_whole_composite_adds_to_its_recipe_cost_on_a_tab_after_its_Products() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3, isProductOwned: true);
        var icingId = await AddIngredientAsync("Icing", costPerGram: 0.1m);
        var state = await CreateLoadedStateAsync();
        await state.NewCompositeCommand.ExecuteAsync();
        state.EditingName = "Party Box";
        state.ComponentsTable.ComponentEdits.Add(Line(state, ensaymadaId, 6));
        state.ComponentsTable.ComponentEdits.Add(Line(state, cheeseRollId, 4));
        await Settle.UntilAsync(state,
            () => state.ComponentsTable.ComponentEdits.All(r => r.RecipeCost is not null),
            () => $"costs={string.Join(", ", state.ComponentsTable.ComponentEdits.Select(r => r.RecipeCost))}");

        // 500 g of icing at 0.10 a gram, 100 g of it on one box: 10.00.
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.EditingUsageRole!.Id.Should().Be(toppingRoleId);
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = icingId, Grams = 500m });
        state.UsageForm.TypeWeightPerPiece(100m);

        await state.SaveCommand.ExecuteAsync();
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1 && state.ComponentsTable.ComponentEdits.Count == 2,
            () => $"sections={state.Recipe.UsageEdits.Count} lines={state.ComponentsTable.ComponentEdits.Count}");

        state.ErrorMessage.Should().BeNull();
        state.Recipe.RecipeTabs.Should().HaveCount(3);
        state.Recipe.RecipeTabs[0].Should().BeSameAs(state.Recipe.ComponentsTab);
        state.Recipe.RecipeTabs[1].Should().BeOfType<SubAssemblyRecipeTab>().Which.Header.Should().Be("Topping");
        state.Costing.Costs.SubAssemblyLines.Select(l => (l.Name, l.Cost)).Should().Equal(
            ("Ensaymada × 6", 15m), ("Cheese Roll × 4", 20m), ("Topping", 10m));
        state.Costing.Costs.SubAssemblies.Should().Be(45m);
        state.ComponentsTable.ComponentsCost.Should().Be(35m, "the Products tab shows what its products bring");
        state.Costing.Costs.PartsTitle.Should().Be("Products and sub-assemblies (ingredients)");
        state.Costing.Costs.PartsBase.Should().Be("products and sub-assemblies");
        CanSave(state).Should().BeFalse();
        await using (var dbContext = CreateDbContext())
        {
            var box = await dbContext.Products.SingleAsync(p => p.Name == "Party Box", Cancellation);
            (await dbContext.SubAssemblies.SingleAsync(s => s.RoleId == toppingRoleId, Cancellation)).OwnerProductId.Should().Be(box.Id);
        }

        // Delete Sub-Assembly on its tab takes it out again, back to the Products tab.
        state.Recipe.PickRecipeTab(state.Recipe.RecipeTabs[1]);
        await state.Recipe.RemoveUsageCommand.ExecuteAsync();

        state.Recipe.UsageEdits.Should().BeEmpty();
        state.Recipe.RecipeTabs.Should().Equal(state.Recipe.ComponentsTab, state.Recipe.PackagingTab);
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.ComponentsTab);
        state.Costing.Costs.SubAssemblies.Should().Be(35m);
        state.Costing.Costs.PartsTitle.Should().Be("Products (ingredients)");
        CanSave(state).Should().BeTrue();
    });

    [Test]
    public Task Save_refuses_a_composite_of_one_product_or_with_a_line_half_filled_in() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        var state = await CreateLoadedStateAsync();
        await state.NewCompositeCommand.ExecuteAsync();
        state.EditingName = "Pasalubong Box";
        state.ComponentsTable.ComponentEdits.Add(Line(state, ensaymadaId, 6));

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("A composite product needs at least two different products.");

        state.ComponentsTable.ComponentEdits.Add(Line(state, cheeseRollId, null));
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("Enter how many Cheese Roll go in, or delete its line.");
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

        var state = await CreateLoadedStateAsync();
        state.Products.Select(p => p.Name).Should().Contain(["Pasalubong Box", "Party Tray"]);

        state.Pick(state.Products.Single(p => p.Id == boxId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.ComponentsTable.ComponentEdits.Count == 3, () => $"lines={state.ComponentsTable.ComponentEdits.Count}");

        state.ErrorMessage.Should().Be(CompositeProductRules.HoldsItselfText("Pasalubong Box"));
        state.ComponentsTable.ComponentEdits[0].Quantity = 7;
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().Be(
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
        var state = await CreateLoadedStateAsync();

        state.Pick(state.Products.Single(p => p.Id == boxId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.ComponentsTable.ComponentEdits.Count == 2, () => $"lines={state.ComponentsTable.ComponentEdits.Count}");

        state.ComponentsTable.ComponentProductOptions.Select(p => p.Name).Should().Equal(
            ["Cheese Roll", "Chocolate Cake", "Ensaymada", "Office Tray"],
            "the Party Package holds the box, and the box can't hold itself; the Office Tray doesn't hold it");

        state.Pick(state.Products.Single(p => p.Name == "Party Package"));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.ComponentsTable.ComponentEdits.Count == 2 && state.ComponentsTable.ComponentEdits[0].ComponentProductId == boxId, () => $"lines={state.ComponentsTable.ComponentEdits.Count}");
        state.ComponentsTable.ComponentProductOptions.Select(p => p.Name).Should().Equal("Cheese Roll", "Chocolate Cake", "Ensaymada", "Office Tray", "Pasalubong Box");

        // A composite's own figures follow what's in it, the box inside it included.
        state.Costing.Costs.SubAssemblyLines.Select(l => (l.Name, l.Cost)).Should().Equal(("Pasalubong Box × 2", 70m), ("Chocolate Cake × 1", 0m));

        // A product of sub-assemblies has no Products tab.
        state.Pick(state.Products.Single(p => p.Id == ensaymadaId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1, () => $"sections={state.Recipe.UsageEdits.Count}");
        state.IsEditingComposite.Should().BeFalse();
        state.Recipe.RecipeTabs.Should().NotContain(state.Recipe.ComponentsTab);
        state.ComponentsTable.ComponentEdits.Should().BeEmpty();
        state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single().Usage.RoleId.Should().Be(doughRoleId);
    });

    [Test]
    public Task A_quantity_typed_offers_Save_while_its_cell_has_the_cursor_and_typed_back_it_doesnt() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        var state = await CreateLoadedStateAsync();
        state.Pick(state.Products.Single(p => p.Id == boxId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.ComponentsTable.ComponentEdits.Count == 2, () => $"lines={state.ComponentsTable.ComponentEdits.Count}");
        CanSave(state).Should().BeFalse();

        // Save follows the row itself, not the costs it moves: those are only worked out as they're read, and nothing
        // here reads them, as nothing does between two keys typed before the screen is drawn again.
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        state.ComponentsTable.ComponentEdits[0].Quantity = 7;
        CanSave(state).Should().BeTrue("the quantity is still being typed, and Save's click is what ends it");
        state.ComponentsTable.ComponentEdits[0].Quantity = 6;
        CanSave(state).Should().BeFalse("typed back, the lines are as saved");
        recorder.EndUndoStep();
    });

    [Test]
    public Task A_lines_edit_is_one_undo_step_and_Save_is_offered_only_while_the_lines_differ() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        var state = await CreateLoadedStateAsync();
        state.Pick(state.Products.Single(p => p.Id == boxId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.ComponentsTable.ComponentEdits.Count == 2, () => $"lines={state.ComponentsTable.ComponentEdits.Count}");
        CanSave(state).Should().BeFalse();

        // What the grid's undo behaviour does around a cell edit.
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        state.ComponentsTable.ComponentEdits[0].Quantity = 8;
        recorder.EndUndoStep();

        state.Costing.Costs.SubAssemblies.Should().Be(40m);
        CanSave(state).Should().BeTrue();

        await state.UndoCommand.ExecuteAsync();

        state.ComponentsTable.ComponentEdits.Select(r => (r.ComponentProductId, r.Quantity)).Should().Equal((ensaymadaId, 6), (cheeseRollId, 4));
        state.Costing.Costs.SubAssemblies.Should().Be(35m);
        CanSave(state).Should().BeFalse();

        await state.RedoCommand.ExecuteAsync();

        state.ComponentsTable.ComponentEdits[0].Quantity.Should().Be(8);
        state.Costing.Costs.SubAssemblies.Should().Be(40m);

        // Remove Line is a step of its own too.
        state.ComponentsTable.SelectedComponentEdit = state.ComponentsTable.ComponentEdits[1];
        await state.ComponentsTable.RemoveLineCommand.ExecuteAsync();
        state.ComponentsTable.ComponentEdits.Should().ContainSingle();
        await state.UndoCommand.ExecuteAsync();
        state.ComponentsTable.ComponentEdits.Should().HaveCount(2);
    });

    [Test]
    public Task Including_each_products_packaging_adds_it_to_the_composites_packaging_as_one_undo_step_and_Save_keeps_it() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        await AddOwnPackagingAsync(ensaymadaId, "Ensaymada Bag", 1.50m);
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        var state = await CreateLoadedStateAsync();
        state.Pick(state.Products.Single(p => p.Id == boxId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.ComponentsTable.ComponentEdits.Count == 2, () => $"lines={state.ComponentsTable.ComponentEdits.Count}");

        state.ComponentsTable.IncludesPackaging.Should().BeFalse();
        state.Costing.Costs.Packaging.Should().Be(0m, "a product's own packaging stays out until the box is ticked");
        state.Costing.Costs.PackagingTitle.Should().Be("Packaging");
        state.ComponentsTable.Caption.Should().Contain("Its own packaging stays out");
        state.ComponentsTable.ComponentEdits.Select(r => (r.PackagingCost, r.Cost)).Should().Equal((1.50m, 15m), (0m, 20m));
        state.ComponentsTable.ComponentsCost.Should().Be(35m);

        // What ticking the box does.
        state.ComponentsTable.IsPackagingTicked = true;

        // The Products table: each line's Cost is its recipe and its packaging, and the figure above adds them up.
        state.ComponentsTable.ComponentEdits.Select(r => (r.RecipeCost, r.PackagingCost, r.Cost)).Should().Equal((2.50m, 1.50m, 24m), (5m, 0m, 20m));
        state.ComponentsTable.ComponentsCost.Should().Be(44m);
        PrintedComponent.From(state.ComponentsTable.ComponentEdits).Should().Equal(
            new PrintedComponent("Ensaymada", 6, 2.50m, 24m, 1.50m), new PrintedComponent("Cheese Roll", 4, 5m, 20m, 0m));

        // The costing still keeps them apart: the products' lines are their recipe, and their packaging is packaging.
        state.Costing.Costs.SubAssemblyLines.Select(l => (l.Name, l.Cost)).Should().Equal(("Ensaymada × 6", 15m), ("Cheese Roll × 4", 20m));

        state.ComponentsTable.IncludesPackaging.Should().BeTrue();
        state.Costing.Costs.ComponentPackaging.Should().Be(9m, "6 bags of 1.50; Cheese Roll has none");
        state.Costing.Costs.Packaging.Should().Be(9m);
        state.Costing.Costs.OwnPackaging.Should().Be(0m, "the Packaging tab lists the composite's own lines only");
        state.Costing.Costs.PackagingTitle.Should().Be("Packaging (own and products')");
        state.Costing.Costs.SubAssemblies.Should().Be(35m, "the recipe cost is unchanged");
        state.Costing.Costs.Material.Should().Be(44m);
        state.ComponentsTable.Caption.Should().Contain("and its own packaging");
        CanSave(state).Should().BeTrue();

        await state.UndoCommand.ExecuteAsync();

        state.ComponentsTable.IncludesPackaging.Should().BeFalse();
        state.Costing.Costs.Packaging.Should().Be(0m);
        state.ComponentsTable.ComponentEdits.Select(r => r.Cost).Should().Equal(15m, 20m);
        state.ComponentsTable.ComponentsCost.Should().Be(35m);
        CanSave(state).Should().BeFalse();

        await state.RedoCommand.ExecuteAsync();
        state.Costing.Costs.Packaging.Should().Be(9m);

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.Products.SingleAsync(p => p.Id == boxId, Cancellation)).IncludesComponentPackaging.Should().BeTrue();
        }

        state.ComponentsTable.IncludesPackaging.Should().BeTrue();
        state.Costing.Costs.Packaging.Should().Be(9m);
        // The rows reloaded after Save follow the box too.
        state.ComponentsTable.ComponentEdits.Select(r => r.Cost).Should().Equal(24m, 20m);
        CanSave(state).Should().BeFalse();

        // A new composite starts with its products' packaging left out.
        await state.NewCompositeCommand.ExecuteAsync();
        state.ComponentsTable.IncludesPackaging.Should().BeFalse();
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
        var state = await CreateLoadedStateAsync();
        state.Pick(state.Products.Single(p => p.Id == boxId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.ComponentsTable.ComponentEdits.Count == 2, () => $"lines={state.ComponentsTable.ComponentEdits.Count}");

        // Ensaymada's pieces are made twice the size elsewhere: each now takes 5.00 of recipe.
        var doughId = await SubAssemblyIdAsync(ensaymadaId, doughRoleId);
        await SaveRecipeAsync(ensaymadaId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 100m)]));

        await state.LoadCommand.ExecuteAsync();

        state.ComponentsTable.ComponentEdits[0].RecipeCost.Should().Be(5m);
        state.Costing.Costs.SubAssemblies.Should().Be(50m);
        CanSave(state).Should().BeFalse("a product repriced elsewhere isn't this product's change");
    });

    [Test]
    public Task A_product_a_composite_holds_isnt_deleted_and_the_message_names_the_composite() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        var state = await CreateLoadedStateAsync();
        state.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));
        state.Pick(state.Products.Single(p => p.Id == ensaymadaId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1, () => $"sections={state.Recipe.UsageEdits.Count}");

        await state.DeleteCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("Pasalubong Box holds this product. Take it out there first.");
        state.Products.Should().Contain(p => p.Id == ensaymadaId);
    });

    [Test]
    public Task A_composites_printout_has_its_products_table_and_its_costing_per_unit() => UiThread.RunAsync(async () =>
    {
        var (ensaymadaId, cheeseRollId, _) = await AddTwoProductsAsync();
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        var state = await CreateLoadedStateAsync();
        state.Pick(state.Products.Single(p => p.Id == boxId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.ComponentsTable.ComponentEdits.Count == 2, () => $"lines={state.ComponentsTable.ComponentEdits.Count}");

        var printout = state.PreparePrintout().Value.Should().BeOfType<ProductPrintout>().Subject;

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
        var state = await CreateLoadedStateAsync();
        var production = state.View.List.ProductTree.StageFolder(ProductStage.Production);

        state.View.List.TreeMenuFor(null).Select(i => i.Header).Should().StartWith(["New Product", "New Composite Product"]);
        var here = state.View.List.TreeMenuFor(production).Single(i => i.Header == "New Composite Product Here");

        await ((ScreenCommand<ProductFolderNode?>)here.Command!).ExecuteAsync((ProductFolderNode?)here.Parameter);

        state.IsEditingComposite.Should().BeTrue();
        state.NewProductPlace.Stage.Should().Be(ProductStage.Production);
        state.NewProductTag.Should().Be("Goes in Production");
    });

    /// <summary>Whether the Save button would be enabled: the command's own CanExecute, as the page's button reads it.</summary>
    private static bool CanSave(ProductForm state) => state.SaveCommand.CanExecute();

    private async Task<ProductForm> CreateLoadedStateAsync()
    {
        var factory = CreateDbContextFactory();
        var state = ProductsViews.Create(factory, CreateUnitOfWork()).Form!;
        await state.LoadCommand.ExecuteAsync();
        return state;
    }
}
