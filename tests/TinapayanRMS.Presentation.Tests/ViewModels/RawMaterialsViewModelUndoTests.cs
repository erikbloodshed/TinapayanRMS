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
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>
/// Undo and redo on the Raw Materials screen, the same mechanism Products uses with its own snapshot, and with it
/// whether Save has anything to write, which is measured against the same snapshots. The price history is
/// deliberately outside it: its Delete writes to the database on the spot, so there is nothing unsaved for an
/// undo to put back.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class RawMaterialsViewModelUndoTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task Undoing_a_name_edit_puts_it_back() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();

        Edit(viewModel, () => viewModel.EditingName = "Renamed");
        viewModel.History.CanUndo.Should().BeTrue();

        viewModel.TryUndo().Should().BeTrue();
        viewModel.EditingName.Should().Be("Flour");
        viewModel.History.CanRedo.Should().BeTrue();

        viewModel.TryRedo().Should().BeTrue();
        viewModel.EditingName.Should().Be("Renamed");
    });

    [Test]
    public Task Undoing_an_alternative_units_edit_keeps_its_id_so_Save_updates_it() => UiThread.RunAsync(async () =>
    {
        // RawMaterialUnitRow writes straight through into the stored RawMaterialUnit, and Save hands those very
        // entities to the repository — a unit that lost its id would be saved as a new one and the stored one deleted.
        var (viewModel, materialId) = await OpenPurchasedIngredientAsync();
        var savedUnitId = viewModel.Units.Rows.Should().ContainSingle().Subject.Unit.Id;
        savedUnitId.Should().NotBe(0);

        Edit(viewModel, () => viewModel.Units.Rows[0].BaseUnitsPerUnit = 50000m);
        viewModel.TryUndo().Should().BeTrue();

        viewModel.Units.Rows[0].BaseUnitsPerUnit.Should().Be(25000m);
        viewModel.Units.Rows[0].Unit.Id.Should().Be(savedUnitId, "an id of 0 would make Save add a replacement");

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        var unit = await dbContext.RawMaterialUnits.SingleAsync(u => u.RawMaterialId == materialId, Cancellation);
        unit.Id.Should().Be(savedUnitId, "the stored unit was updated, not deleted and recreated");
        unit.BaseUnitsPerUnit.Should().Be(25000m);
    });

    [Test]
    public Task Undoing_a_removed_alternative_unit_puts_it_back() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();
        viewModel.Units.SelectedRow = viewModel.Units.Rows[0];

        await viewModel.Units.DeleteCommand.Execute();
        viewModel.Units.Rows.Should().BeEmpty();

        viewModel.TryUndo().Should().BeTrue();
        viewModel.Units.Rows.Should().ContainSingle().Which.BaseUnitsPerUnit.Should().Be(25000m);
    });

    [Test]
    public Task Undoing_a_brand_price_puts_the_row_back_as_it_was() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();

        Edit(viewModel, () =>
        {
            var row = viewModel.PurchaseCost.NewBrandPriceRow();
            row.Brand = "Baker's Best";
            row.UnitKey = viewModel.Units.Rows[0].UnitOfMeasureId!.Value;
            row.Price = 1200m;
            viewModel.PurchaseCost.BrandPriceRows.Add(row);
        });
        viewModel.PurchaseCost.BrandPriceRows.Should().ContainSingle();

        viewModel.TryUndo().Should().BeTrue();
        viewModel.PurchaseCost.BrandPriceRows.Should().BeEmpty();

        viewModel.TryRedo().Should().BeTrue();
        var restored = viewModel.PurchaseCost.BrandPriceRows.Should().ContainSingle().Subject;
        restored.Brand.Should().Be("Baker's Best");
        restored.Price.Should().Be(1200m);
    });

    [Test]
    public Task Undo_leaves_what_Save_writes_alone() => UiThread.RunAsync(async () =>
    {
        var (viewModel, materialId) = await OpenPurchasedIngredientAsync();

        Edit(viewModel, () => viewModel.EditingName = "Renamed");
        viewModel.TryUndo().Should().BeTrue();

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(m => m.Id == materialId, Cancellation)).Name
            .Should().Be("Flour", "a change undone and a change never made come to the same thing");
    });

    [Test]
    public Task A_step_that_changes_nothing_is_not_recorded() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();
        var recorder = (IUndoRecorder)viewModel;

        recorder.BeginUndoStep();
        recorder.EndUndoStep();
        viewModel.History.CanUndo.Should().BeFalse();

        Edit(viewModel, () => viewModel.EditingName = "Flour");
        viewModel.History.CanUndo.Should().BeFalse("it was retyped to the name it already had");
    });

    [Test]
    public Task The_history_goes_when_the_form_stops_being_the_one_it_recorded() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();

        Edit(viewModel, () => viewModel.EditingName = "Renamed");
        viewModel.History.CanUndo.Should().BeTrue();

        // A Save reloads what was saved, which re-points the selection and so reloads the form.
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady, () => $"ready={viewModel.AreDetailsReady}");
        viewModel.History.CanUndo.Should().BeFalse("what's saved is the new starting point");

        Edit(viewModel, () => viewModel.EditingName = "Renamed again");
        viewModel.History.CanUndo.Should().BeTrue();

        // New over unsaved changes asks about them first: not saving them is how they're left behind.
        viewModel.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        await viewModel.NewCommand.Execute();
        viewModel.History.CanUndo.Should().BeFalse("a new material has no history of its own");
    });

    [Test]
    public Task A_step_taken_before_the_details_arrive_is_not_recorded() => UiThread.RunAsync(async () =>
    {
        // The brand prices and the BOM land a moment after the pick, which is what AreDetailsReady describes. A step
        // taken in that window holds empty grids, so undoing it would put them back over the details that arrived.
        var materialId = await AddIngredientAsync("Flour");
        await CreateBrandPriceRepository().SetAsync(
            materialId, [new RawMaterialBrandPrice { Brand = "Baker's Best", Price = 0.05m }], Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(m => m.Id == materialId);
        viewModel.AreDetailsReady.Should().BeFalse("the details are still on their way");
        Edit(viewModel, () => viewModel.EditingName = "Renamed");

        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && viewModel.PurchaseCost.BrandPriceRows.Count == 1,
            () => $"ready={viewModel.AreDetailsReady}, brands={viewModel.PurchaseCost.BrandPriceRows.Count}");

        viewModel.History.CanUndo.Should().BeFalse("nothing typed before the details arrived is worth a step");
        viewModel.TryUndo().Should().BeFalse();
        viewModel.PurchaseCost.BrandPriceRows.Should().ContainSingle("the brand prices that arrived are still showing");
    });

    [Test]
    public Task Undoing_an_add_in_edit_puts_the_headers_totals_back() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenMadeIngredientAsync();
        viewModel.Recipe.BomTotalWeight.Should().Be(970m, "1,000 g less the 3% weight loss");
        viewModel.Recipe.BomTotalCost.Should().Be(110m, "1,000 g of sugar at its buffered ₱0.11");

        Edit(viewModel, () => viewModel.Recipe.AddIns.Single().Grams = 0m);
        viewModel.Recipe.BomTotalWeight.Should().Be(485m, "the recipe's 500 g, less 3%");
        viewModel.Recipe.BomTotalCost.Should().Be(55m);

        viewModel.TryUndo().Should().BeTrue();
        viewModel.Recipe.BomTotalWeight.Should().Be(970m, "the restore works the totals out again");
        viewModel.Recipe.BomTotalCost.Should().Be(110m);

        viewModel.TryRedo().Should().BeTrue();
        viewModel.Recipe.BomTotalWeight.Should().Be(485m);
        viewModel.Recipe.BomTotalCost.Should().Be(55m);
    });

    [Test]
    public Task Undoing_another_sub_assembly_picked_puts_the_recipe_and_its_totals_back() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenMadeIngredientAsync();

        Edit(viewModel, () => viewModel.Recipe.SubAssembly = viewModel.Recipe.SubAssemblyOptions.Single(s => s.Name == "Thin Syrup Base"));
        await Settle.UntilAsync(viewModel, () => viewModel.Recipe.Lines.Count == 2, () => $"recipe lines={viewModel.Recipe.Lines.Count}");
        viewModel.Recipe.BomTotalWeight.Should().Be(1455m, "1,000 g of the thinner recipe and the 500 g add-in, less 3%");

        viewModel.TryUndo().Should().BeTrue();
        await Settle.UntilAsync(viewModel, () => viewModel.Recipe.Lines.Count == 1, () => $"recipe lines={viewModel.Recipe.Lines.Count}");
        viewModel.Recipe.SubAssembly!.Name.Should().Be("Syrup Base");
        viewModel.Recipe.BomTotalWeight.Should().Be(970m);
        viewModel.History.IsDirty.Should().BeFalse("it's back as it was saved");
    });

    [Test]
    public Task Undoing_a_weight_loss_edit_puts_the_headers_yield_back() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenMadeIngredientAsync();

        Edit(viewModel, () => viewModel.EditingWeightLossPercent = 20m);
        viewModel.Recipe.BomTotalWeight.Should().Be(800m);
        viewModel.Recipe.BomTotalCost.Should().Be(110m, "the weight loss doesn't move the cost");

        viewModel.TryUndo().Should().BeTrue();
        viewModel.EditingWeightLossPercent.Should().Be(3m);
        viewModel.Recipe.BomTotalWeight.Should().Be(970m);
    });

    [Test]
    public Task Save_is_not_offered_until_the_form_is_changed() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();

        viewModel.History.IsDirty.Should().BeFalse("the load itself isn't a change");
        (await CanSave(viewModel)).Should().BeFalse();

        Edit(viewModel, () => viewModel.EditingName = "Flour, Renamed");

        (await CanSave(viewModel)).Should().BeTrue();
    });

    [Test]
    public Task Save_stops_being_offered_once_the_change_is_taken_back() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();
        Edit(viewModel, () => viewModel.Units.Rows[0].BaseUnitsPerUnit = 50000m);
        (await CanSave(viewModel)).Should().BeTrue();

        viewModel.TryUndo().Should().BeTrue();

        (await CanSave(viewModel)).Should().BeFalse("the form shows what was saved again");
    });

    [Test]
    public Task Save_stops_being_offered_once_it_has_written() => UiThread.RunAsync(async () =>
    {
        var (viewModel, materialId) = await OpenPurchasedIngredientAsync();
        Edit(viewModel, () => viewModel.EditingName = "Flour, Renamed");

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && viewModel.SelectedRawMaterial?.Id == materialId,
            () => $"ready={viewModel.AreDetailsReady}, selected={viewModel.SelectedRawMaterial?.Id}");

        (await CanSave(viewModel)).Should().BeFalse("what was written is the new starting point");
    });

    [Test]
    public Task Save_is_not_offered_on_a_blank_new_form() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();

        await viewModel.NewCommand.Execute();
        (await CanSave(viewModel)).Should().BeFalse("an empty form has nothing to write");

        Edit(viewModel, () => viewModel.EditingName = "Bread Flour");
        (await CanSave(viewModel)).Should().BeTrue();
    });

    [Test]
    public Task A_cell_still_open_keeps_Save_offered() => UiThread.RunAsync(async () =>
    {
        // What's typed into a cell doesn't reach the row until the edit ends, and the click that saves is the
        // click that ends it - a Save disabled under the cursor would swallow it.
        var (viewModel, _) = await OpenPurchasedIngredientAsync();
        var recorder = (IUndoRecorder)viewModel;

        recorder.IsEditPending = true;
        (await CanSave(viewModel)).Should().BeTrue();

        recorder.IsEditPending = false;
        (await CanSave(viewModel)).Should().BeFalse("the cell closed with nothing typed");
    });

    [Test]
    public Task Save_is_not_offered_on_the_blank_form_the_screen_opens_with() => UiThread.RunAsync(async () =>
    {
        await GetOrCreateUnitOfMeasureAsync("kilogram", "kg");

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        (await CanSave(viewModel)).Should().BeFalse("an empty form has nothing to write");
    });

    [Test]
    public Task Showing_the_tab_again_is_not_something_to_save() => UiThread.RunAsync(async () =>
    {
        // LoadCommand runs every time the tab is shown; it only merges in fresh data and re-points the pickers.
        var (viewModel, _) = await OpenPurchasedIngredientAsync();

        await viewModel.LoadCommand.Execute();

        (await CanSave(viewModel)).Should().BeFalse("a refresh isn't an edit");
    });

    /// <summary>Whether the Save button would be enabled: the command's own CanExecute, as the page's button reads it.</summary>
    private static async Task<bool> CanSave(RawMaterialsViewModel viewModel) =>
        await viewModel.SaveCommand.CanExecute.FirstAsync();

    /// <summary>One finished change, as the fields and line tables bracket a gesture in the app.</summary>
    private static void Edit(RawMaterialsViewModel viewModel, Action change)
    {
        var recorder = (IUndoRecorder)viewModel;
        recorder.BeginUndoStep();
        change();
        recorder.EndUndoStep();
    }

    /// <summary>A saved purchased ingredient with one alternative unit (a 25 kg bag), open in the form.</summary>
    [Test]
    public Task Re_apply_Target_types_the_buffer_last_typed_again_once_a_price_has_worn_it_down() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();

        // A 25 kg bag at 1,250, 0.05 a gram, the 10% buffer on top: 0.055. Then it rises to 1,300, 0.052, and the buffer wears to 5.77%.
        var brand = viewModel.PurchaseCost.NewBrandPriceRow();
        brand.UnitKey = viewModel.Units.Rows[0].UnitOfMeasureId!.Value;
        brand.Price = 1250m;
        viewModel.PurchaseCost.BrandPriceRows.Add(brand);
        viewModel.PurchaseCost.TypedCostBufferPercent = 10m;
        viewModel.PurchaseCost.EditingBufferedCost.Should().Be(0.055m);
        viewModel.PurchaseCost.IsBufferOffTarget.Should().BeFalse();

        brand.Price = 1300m;
        viewModel.PurchaseCost.IsBufferOffTarget.Should().BeTrue();
        viewModel.PurchaseCost.TargetBufferText.Should().Be("Target 10%");

        await viewModel.PurchaseCost.ReapplyTargetBufferCommand.Execute();

        viewModel.PurchaseCost.EditingCostBufferPercent.Should().Be(10m);
        viewModel.PurchaseCost.EditingBufferedCost.Should().Be(0.0572m);
        viewModel.PurchaseCost.IsBufferOffTarget.Should().BeFalse();
        viewModel.TryUndo().Should().BeTrue("it's one step to take back");
        viewModel.PurchaseCost.EditingBufferedCost.Should().Be(0.055m);
    });

    [Test]
    public Task Cost_From_costs_the_material_at_the_brand_picked_and_undoing_it_puts_the_buffer_box_back() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();

        // Kingbake at 1,300 a 25 kg bag (0.052/g) and Gold at 1,100 (0.044/g); 10% on the dearest is 0.0572.
        Edit(viewModel, () =>
        {
            foreach (var (brand, price) in new[] { ("Kingbake", 1300m), ("Gold", 1100m) })
            {
                var row = viewModel.PurchaseCost.NewBrandPriceRow();
                row.Brand = brand;
                row.UnitKey = viewModel.Units.Rows[0].UnitOfMeasureId!.Value;
                row.Price = price;
                viewModel.PurchaseCost.BrandPriceRows.Add(row);
            }

            viewModel.PurchaseCost.TypedCostBufferPercent = 10m;
        });
        viewModel.PurchaseCost.CostFromOptions.Select(o => o.Label).Should().Equal("Dearest brand", "Kingbake", "Gold");
        viewModel.PurchaseCost.DearestPurchaseCost.Should().Be(0.052m);

        Edit(viewModel, () => viewModel.PurchaseCost.CostFrom = viewModel.PurchaseCost.CostFromOptions.Single(o => o.Brand == "Gold"));

        viewModel.PurchaseCost.EditingPreferredBrand.Should().Be("Gold");
        viewModel.PurchaseCost.DearestPurchaseCost.Should().Be(0.044m, "Gold's price is what Save costs it at");
        viewModel.PurchaseCost.EditingCostBufferPercent.Should().Be(30m, "the fixed 0.0572 is 30% over Gold's 0.044");
        viewModel.PurchaseCost.PurchaseBufferedCostText.Should().Contain("over Gold's");

        List<string?> raised = [];
        viewModel.PurchaseCost.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        viewModel.TryUndo().Should().BeTrue();

        viewModel.PurchaseCost.EditingPreferredBrand.Should().BeNull();
        viewModel.PurchaseCost.EditingCostBufferPercent.Should().Be(10m);
        raised.Should().Contain(nameof(RawMaterialPurchaseCost.TypedCostBufferPercent), "the Cost Buffer % box shows it");
    });

    private async Task<(RawMaterialsViewModel ViewModel, int MaterialId)> OpenPurchasedIngredientAsync()
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

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(m => m.Id == materialId);
        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && viewModel.Units.Rows.Count == 1,
            () => $"ready={viewModel.AreDetailsReady}, units={viewModel.Units.Rows.Count}");

        viewModel.History.CanUndo.Should().BeFalse("the load itself isn't a change");
        return (viewModel, materialId);
    }

    /// <summary>
    /// A saved made ingredient (3% weight loss) made from a recipe of 500 g of sugar ("Syrup Base"), with an add-in of 500 g
    /// of brown sugar, each at a buffered ₱0.11/g, open in the form. "Thin Syrup Base" (500 g of sugar, 500 g of water) is
    /// another recipe it could be made from.
    /// </summary>
    private async Task<(RawMaterialsViewModel ViewModel, int MaterialId)> OpenMadeIngredientAsync()
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

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(m => m.Id == materialId);
        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && viewModel.Recipe.AddIns.Count == 1 && viewModel.Recipe.Lines.Count == 1,
            () => $"ready={viewModel.AreDetailsReady}, add-ins={viewModel.Recipe.AddIns.Count}, recipe lines={viewModel.Recipe.Lines.Count}");

        viewModel.History.CanUndo.Should().BeFalse("the load itself isn't a change");
        return (viewModel, materialId);
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

    private RawMaterialsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();

        return new RawMaterialsViewModel(
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
