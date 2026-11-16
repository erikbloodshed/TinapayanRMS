using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.RawMaterials;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.RawMaterials;

/// <summary>
/// Undo and redo on the Raw Materials screen, the same mechanism Products uses with its own snapshot, and with it
/// whether Save has anything to write, which is measured against the same snapshots. The price history is
/// deliberately outside it: its Delete writes to the database on the spot, so there is nothing unsaved for an
/// undo to put back.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class RawMaterialsStateUndoTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task Undoing_a_name_edit_puts_it_back() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenPurchasedIngredientAsync();

        Edit(state, () => state.EditingName = "Renamed");
        state.History.CanUndo.Should().BeTrue();

        state.TryUndo().Should().BeTrue();
        state.EditingName.Should().Be("Flour");
        state.History.CanRedo.Should().BeTrue();

        state.TryRedo().Should().BeTrue();
        state.EditingName.Should().Be("Renamed");
    });

    [Test]
    public Task Undoing_an_alternative_units_edit_keeps_its_id_so_Save_updates_it() => UiThread.RunAsync(async () =>
    {
        // RawMaterialUnitRow writes straight through into the stored RawMaterialUnit, and Save hands those very
        // entities to the repository — a unit that lost its id would be saved as a new one and the stored one deleted.
        var (state, materialId) = await OpenPurchasedIngredientAsync();
        var savedUnitId = state.Units.Rows.Should().ContainSingle().Subject.Unit.Id;
        savedUnitId.Should().NotBe(0);

        Edit(state, () => state.Units.Rows[0].BaseUnitsPerUnit = 50000m);
        state.TryUndo().Should().BeTrue();

        state.Units.Rows[0].BaseUnitsPerUnit.Should().Be(25000m);
        state.Units.Rows[0].Unit.Id.Should().Be(savedUnitId, "an id of 0 would make Save add a replacement");

        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        var unit = await dbContext.RawMaterialUnits.SingleAsync(u => u.RawMaterialId == materialId, Cancellation);
        unit.Id.Should().Be(savedUnitId, "the stored unit was updated, not deleted and recreated");
        unit.BaseUnitsPerUnit.Should().Be(25000m);
    });

    [Test]
    public Task Undoing_a_removed_alternative_unit_puts_it_back() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenPurchasedIngredientAsync();
        state.Units.SelectedRow = state.Units.Rows[0];

        await state.Units.DeleteCommand.ExecuteAsync();
        state.Units.Rows.Should().BeEmpty();

        state.TryUndo().Should().BeTrue();
        state.Units.Rows.Should().ContainSingle().Which.BaseUnitsPerUnit.Should().Be(25000m);
    });

    [Test]
    public Task Undoing_a_brand_price_puts_the_row_back_as_it_was() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenPurchasedIngredientAsync();

        Edit(state, () =>
        {
            var row = state.PurchaseCost.NewBrandPriceRow();
            row.Brand = "Baker's Best";
            row.UnitKey = state.Units.Rows[0].UnitOfMeasureId!.Value;
            row.Price = 1200m;
            state.PurchaseCost.BrandPriceRows.Add(row);
        });
        state.PurchaseCost.BrandPriceRows.Should().ContainSingle();

        state.TryUndo().Should().BeTrue();
        state.PurchaseCost.BrandPriceRows.Should().BeEmpty();

        state.TryRedo().Should().BeTrue();
        var restored = state.PurchaseCost.BrandPriceRows.Should().ContainSingle().Subject;
        restored.Brand.Should().Be("Baker's Best");
        restored.Price.Should().Be(1200m);
    });

    [Test]
    public Task Undo_leaves_what_Save_writes_alone() => UiThread.RunAsync(async () =>
    {
        var (state, materialId) = await OpenPurchasedIngredientAsync();

        Edit(state, () => state.EditingName = "Renamed");
        state.TryUndo().Should().BeTrue();

        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(m => m.Id == materialId, Cancellation)).Name
            .Should().Be("Flour", "a change undone and a change never made come to the same thing");
    });

    [Test]
    public Task A_step_that_changes_nothing_is_not_recorded() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenPurchasedIngredientAsync();
        var recorder = (IUndoRecorder)state;

        recorder.BeginUndoStep();
        recorder.EndUndoStep();
        state.History.CanUndo.Should().BeFalse();

        Edit(state, () => state.EditingName = "Flour");
        state.History.CanUndo.Should().BeFalse("it was retyped to the name it already had");
    });

    [Test]
    public Task The_history_goes_when_the_form_stops_being_the_one_it_recorded() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenPurchasedIngredientAsync();

        Edit(state, () => state.EditingName = "Renamed");
        state.History.CanUndo.Should().BeTrue();

        // A Save reloads what was saved, which re-points the selection and so reloads the form.
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => $"ready={state.AreDetailsReady}");
        state.History.CanUndo.Should().BeFalse("what's saved is the new starting point");

        Edit(state, () => state.EditingName = "Renamed again");
        state.History.CanUndo.Should().BeTrue();

        // New over unsaved changes asks about them first: not saving them is how they're left behind.
        state.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        await state.NewCommand.ExecuteAsync();
        state.History.CanUndo.Should().BeFalse("a new material has no history of its own");
    });

    [Test]
    public Task A_step_taken_before_the_details_arrive_is_not_recorded() => UiThread.RunAsync(async () =>
    {
        // The brand prices and the BOM land a moment after the pick, which is what AreDetailsReady describes. A step
        // taken in that window holds empty grids, so undoing it would put them back over the details that arrived.
        var materialId = await AddIngredientAsync("Flour");
        await CreateBrandPriceRepository().SetAsync(
            materialId, [new RawMaterialBrandPrice { Brand = "Baker's Best", Price = 0.05m }], Cancellation);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.SelectedRawMaterial = state.RawMaterials.Single(m => m.Id == materialId);
        state.AreDetailsReady.Should().BeFalse("the details are still on their way");
        Edit(state, () => state.EditingName = "Renamed");

        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.PurchaseCost.BrandPriceRows.Count == 1,
            () => $"ready={state.AreDetailsReady}, brands={state.PurchaseCost.BrandPriceRows.Count}");

        state.History.CanUndo.Should().BeFalse("nothing typed before the details arrived is worth a step");
        state.TryUndo().Should().BeFalse();
        state.PurchaseCost.BrandPriceRows.Should().ContainSingle("the brand prices that arrived are still showing");
    });

    [Test]
    public Task Undoing_an_add_in_edit_puts_the_headers_totals_back() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenMadeIngredientAsync();
        state.Recipe.BomTotalWeight.Should().Be(970m, "1,000 g less the 3% weight loss");
        state.Recipe.BomTotalCost.Should().Be(110m, "1,000 g of sugar at its buffered ₱0.11");

        Edit(state, () => state.Recipe.AddIns.Single().Grams = 0m);
        state.Recipe.BomTotalWeight.Should().Be(485m, "the recipe's 500 g, less 3%");
        state.Recipe.BomTotalCost.Should().Be(55m);

        state.TryUndo().Should().BeTrue();
        state.Recipe.BomTotalWeight.Should().Be(970m, "the restore works the totals out again");
        state.Recipe.BomTotalCost.Should().Be(110m);

        state.TryRedo().Should().BeTrue();
        state.Recipe.BomTotalWeight.Should().Be(485m);
        state.Recipe.BomTotalCost.Should().Be(55m);
    });

    [Test]
    public Task Undoing_another_sub_assembly_picked_puts_the_recipe_and_its_totals_back() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenMadeIngredientAsync();

        Edit(state, () => state.Recipe.SubAssembly = state.Recipe.SubAssemblyOptions.Single(s => s.Name == "Thin Syrup Base"));
        await Settle.UntilAsync(state, () => state.Recipe.Lines.Count == 2, () => $"recipe lines={state.Recipe.Lines.Count}");
        state.Recipe.BomTotalWeight.Should().Be(1455m, "1,000 g of the thinner recipe and the 500 g add-in, less 3%");

        state.TryUndo().Should().BeTrue();
        await Settle.UntilAsync(state, () => state.Recipe.Lines.Count == 1, () => $"recipe lines={state.Recipe.Lines.Count}");
        state.Recipe.SubAssembly!.Name.Should().Be("Syrup Base");
        state.Recipe.BomTotalWeight.Should().Be(970m);
        state.History.IsDirty.Should().BeFalse("it's back as it was saved");
    });

    [Test]
    public Task Undoing_a_weight_loss_edit_puts_the_headers_yield_back() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenMadeIngredientAsync();

        Edit(state, () => state.EditingWeightLossPercent = 20m);
        state.Recipe.BomTotalWeight.Should().Be(800m);
        state.Recipe.BomTotalCost.Should().Be(110m, "the weight loss doesn't move the cost");

        state.TryUndo().Should().BeTrue();
        state.EditingWeightLossPercent.Should().Be(3m);
        state.Recipe.BomTotalWeight.Should().Be(970m);
    });

    [Test]
    public Task Save_is_not_offered_until_the_form_is_changed() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenPurchasedIngredientAsync();

        state.History.IsDirty.Should().BeFalse("the load itself isn't a change");
        CanSave(state).Should().BeFalse();

        Edit(state, () => state.EditingName = "Flour, Renamed");

        CanSave(state).Should().BeTrue();
    });

    [Test]
    public Task Save_stops_being_offered_once_the_change_is_taken_back() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenPurchasedIngredientAsync();
        Edit(state, () => state.Units.Rows[0].BaseUnitsPerUnit = 50000m);
        CanSave(state).Should().BeTrue();

        state.TryUndo().Should().BeTrue();

        CanSave(state).Should().BeFalse("the form shows what was saved again");
    });

    [Test]
    public Task Save_stops_being_offered_once_it_has_written() => UiThread.RunAsync(async () =>
    {
        var (state, materialId) = await OpenPurchasedIngredientAsync();
        Edit(state, () => state.EditingName = "Flour, Renamed");

        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.SelectedRawMaterial?.Id == materialId,
            () => $"ready={state.AreDetailsReady}, selected={state.SelectedRawMaterial?.Id}");

        CanSave(state).Should().BeFalse("what was written is the new starting point");
    });

    [Test]
    public Task Save_is_not_offered_on_a_blank_new_form() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenPurchasedIngredientAsync();

        await state.NewCommand.ExecuteAsync();
        CanSave(state).Should().BeFalse("an empty form has nothing to write");

        Edit(state, () => state.EditingName = "Bread Flour");
        CanSave(state).Should().BeTrue();
    });

    [Test]
    public Task A_cell_still_open_keeps_Save_offered() => UiThread.RunAsync(async () =>
    {
        // What's typed into a cell doesn't reach the row until the edit ends, and the click that saves is the
        // click that ends it - a Save disabled under the cursor would swallow it.
        var (state, _) = await OpenPurchasedIngredientAsync();
        var recorder = (IUndoRecorder)state;

        recorder.IsEditPending = true;
        CanSave(state).Should().BeTrue();

        recorder.IsEditPending = false;
        CanSave(state).Should().BeFalse("the cell closed with nothing typed");
    });

    [Test]
    public Task Save_is_not_offered_on_the_blank_form_the_screen_opens_with() => UiThread.RunAsync(async () =>
    {
        await GetOrCreateUnitOfMeasureAsync("kilogram", "kg");

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        CanSave(state).Should().BeFalse("an empty form has nothing to write");
    });

    [Test]
    public Task Showing_the_tab_again_is_not_something_to_save() => UiThread.RunAsync(async () =>
    {
        // LoadCommand runs every time the tab is shown; it only merges in fresh data and re-points the pickers.
        var (state, _) = await OpenPurchasedIngredientAsync();

        await state.LoadCommand.ExecuteAsync();

        CanSave(state).Should().BeFalse("a refresh isn't an edit");
    });

    /// <summary>Whether the Save button would be enabled: the command's own CanExecute, as the page's button reads it.</summary>
    private static bool CanSave(RawMaterialsState state) => state.SaveCommand.CanExecute();

    /// <summary>One finished change, as the fields and line tables bracket a gesture in the app.</summary>
    private static void Edit(RawMaterialsState state, Action change)
    {
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        change();
        recorder.EndUndoStep();
    }

    /// <summary>A saved purchased ingredient with one alternative unit (a 25 kg bag), open in the form.</summary>
    [Test]
    public Task Re_apply_Target_types_the_buffer_last_typed_again_once_a_price_has_worn_it_down() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenPurchasedIngredientAsync();

        // A 25 kg bag at 1,250, 0.05 a gram, the 10% buffer on top: 0.055. Then it rises to 1,300, 0.052, and the buffer wears to 5.77%.
        var brand = state.PurchaseCost.NewBrandPriceRow();
        brand.UnitKey = state.Units.Rows[0].UnitOfMeasureId!.Value;
        brand.Price = 1250m;
        state.PurchaseCost.BrandPriceRows.Add(brand);
        state.PurchaseCost.TypedCostBufferPercent = 10m;
        state.PurchaseCost.EditingBufferedCost.Should().Be(0.055m);
        state.PurchaseCost.IsBufferOffTarget.Should().BeFalse();

        brand.Price = 1300m;
        state.PurchaseCost.IsBufferOffTarget.Should().BeTrue();
        state.PurchaseCost.TargetBufferText.Should().Be("Target 10%");

        await state.PurchaseCost.ReapplyTargetBufferCommand.ExecuteAsync();

        state.PurchaseCost.EditingCostBufferPercent.Should().Be(10m);
        state.PurchaseCost.EditingBufferedCost.Should().Be(0.0572m);
        state.PurchaseCost.IsBufferOffTarget.Should().BeFalse();
        state.TryUndo().Should().BeTrue("it's one step to take back");
        state.PurchaseCost.EditingBufferedCost.Should().Be(0.055m);
    });

    [Test]
    public Task Cost_From_costs_the_material_at_the_brand_picked_and_undoing_it_puts_the_buffer_box_back() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenPurchasedIngredientAsync();

        // Kingbake at 1,300 a 25 kg bag (0.052/g) and Gold at 1,100 (0.044/g); 10% on the dearest is 0.0572.
        Edit(state, () =>
        {
            foreach (var (brand, price) in new[] { ("Kingbake", 1300m), ("Gold", 1100m) })
            {
                var row = state.PurchaseCost.NewBrandPriceRow();
                row.Brand = brand;
                row.UnitKey = state.Units.Rows[0].UnitOfMeasureId!.Value;
                row.Price = price;
                state.PurchaseCost.BrandPriceRows.Add(row);
            }

            state.PurchaseCost.TypedCostBufferPercent = 10m;
        });
        state.PurchaseCost.CostFromOptions.Select(o => o.Label).Should().Equal("Dearest brand", "Kingbake", "Gold");
        state.PurchaseCost.DearestPurchaseCost.Should().Be(0.052m);

        Edit(state, () => state.PurchaseCost.CostFrom = state.PurchaseCost.CostFromOptions.Single(o => o.Brand == "Gold"));

        state.PurchaseCost.EditingPreferredBrand.Should().Be("Gold");
        state.PurchaseCost.DearestPurchaseCost.Should().Be(0.044m, "Gold's price is what Save costs it at");
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(30m, "the fixed 0.0572 is 30% over Gold's 0.044");
        state.PurchaseCost.PurchaseBufferedCostText.Should().Contain("over Gold's");

        List<string?> raised = [];
        state.PurchaseCost.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        state.TryUndo().Should().BeTrue();

        state.PurchaseCost.EditingPreferredBrand.Should().BeNull();
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(10m);
        raised.Should().Contain(nameof(RawMaterialPurchaseCost.TypedCostBufferPercent), "the Cost Buffer % box shows it");
    });

    private async Task<(RawMaterialsState State, int MaterialId)> OpenPurchasedIngredientAsync()
    {
        var bagId = await GetOrCreateUnitOfMeasureAsync("bag", "bag");
        int materialId;
        await using (var dbContext = CreateDbContext())
        {
            var category = new RawMaterialCategory { Name = "Flour goods" };
            dbContext.RawMaterialCategories.Add(category);
            var material = new RawMaterial
            {
                Name = "Flour",
                Type = RawMaterialType.Ingredient,
                Category = category,
                Source = RawMaterialSource.Purchased,
                Units = [new RawMaterialUnit { UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m }],
            };
            dbContext.RawMaterials.Add(material);
            await dbContext.SaveChangesAsync(Cancellation);
            materialId = material.Id;
        }

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.SelectedRawMaterial = state.RawMaterials.Single(m => m.Id == materialId);
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Units.Rows.Count == 1,
            () => $"ready={state.AreDetailsReady}, units={state.Units.Rows.Count}");

        state.History.CanUndo.Should().BeFalse("the load itself isn't a change");
        return (state, materialId);
    }

    /// <summary>
    /// A saved made ingredient (3% weight loss) made from a recipe of 500 g of sugar ("Syrup Base"), with an add-in of 500 g
    /// of brown sugar, each at a buffered ₱0.11/g, open in the form. "Thin Syrup Base" (500 g of sugar, 500 g of water) is
    /// another recipe it could be made from.
    /// </summary>
    private async Task<(RawMaterialsState State, int MaterialId)> OpenMadeIngredientAsync()
    {
        int materialId;
        RawMaterialCategory category;
        RawMaterial sugar, brownSugar, water;
        await using (var dbContext = CreateDbContext())
        {
            category = new RawMaterialCategory { Name = "Syrup goods" };
            dbContext.RawMaterialCategories.Add(category);
            sugar = Purchased("Sugar", category);
            brownSugar = Purchased("Brown Sugar", category);
            water = Purchased("Water", category);
            dbContext.RawMaterials.AddRange(sugar, brownSugar, water);
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var recipeId = await AddRecipeAsync("Syrup Base", [(sugar.Id, 500m)]);
        await AddRecipeAsync("Thin Syrup Base", [(sugar.Id, 500m), (water.Id, 500m)]);
        await using (var dbContext = CreateDbContext())
        {
            var syrup = new RawMaterial
            {
                Name = "Simple Syrup",
                Type = RawMaterialType.Ingredient,
                CategoryId = category.Id,
                Source = RawMaterialSource.Made,
                WeightLossPercent = 3m,
                SubAssemblyId = recipeId,
            };
            dbContext.RawMaterials.Add(syrup);
            await dbContext.SaveChangesAsync(Cancellation);

            dbContext.IngredientAddInLines.Add(new IngredientAddInLine { IngredientId = syrup.Id, AddInIngredientId = brownSugar.Id, Grams = 500m });
            await dbContext.SaveChangesAsync(Cancellation);
            materialId = syrup.Id;
        }

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.SelectedRawMaterial = state.RawMaterials.Single(m => m.Id == materialId);
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.AddIns.Count == 1 && state.Recipe.Lines.Count == 1,
            () => $"ready={state.AreDetailsReady}, add-ins={state.Recipe.AddIns.Count}, recipe lines={state.Recipe.Lines.Count}");

        state.History.CanUndo.Should().BeFalse("the load itself isn't a change");
        return (state, materialId);
    }

    /// <summary>A purchased ingredient at ₱0.10 a gram, ₱0.11 buffered (water too: only the totals' arithmetic matters).</summary>
    private static RawMaterial Purchased(string name, RawMaterialCategory category) => new()
    {
        Name = name,
        Type = RawMaterialType.Ingredient,
        Category = category,
        Source = RawMaterialSource.Purchased,
        CostPerBaseUnit = 0.10m,
        BufferedCostPerBaseUnit = 0.11m,
    };

    private RawMaterialsState CreateState()
    {
        var factory = CreateDbContextFactory();

        return new RawMaterialsState(
            new RawMaterialRepository(factory),
            new RawMaterialPriceHistoryRepository(factory),
            new RawMaterialBrandPriceRepository(factory),
            new IngredientBomLineRepository(factory),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new RawMaterialCategoryValidator(),
            new RawMaterialValidator(),
            new RawMaterialUnitValidator(),
            new RawMaterialBrandPriceValidator(),
            new UnitOfMeasureRepository(factory),
            new InMemoryViewStateStore(),
            new CostingDefaultsRepository(factory),
            new LoggerConfiguration().CreateLogger());
    }
}
