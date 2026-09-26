using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Fixtures;
using TinapayanRMS.UI;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.Tests.ViewModels;

/// <summary>
/// Undo and redo on the Raw Materials screen, the same mechanism Products uses with its own snapshot, and with it
/// whether Save has anything to write, which is measured against the same snapshots. The price history is
/// deliberately outside it: its Delete writes to the database on the spot, so there is nothing unsaved for an
/// undo to put back.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class RawMaterialsViewModelUndoTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task Undoing_a_name_edit_puts_it_back()
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();

        Edit(viewModel, () => viewModel.EditingName = "Renamed");
        viewModel.History.CanUndo.Should().BeTrue();

        viewModel.TryUndo().Should().BeTrue();
        viewModel.EditingName.Should().Be("Flour");
        viewModel.History.CanRedo.Should().BeTrue();

        viewModel.TryRedo().Should().BeTrue();
        viewModel.EditingName.Should().Be("Renamed");
    }

    [Fact]
    public async Task Undoing_an_alternative_units_edit_keeps_its_id_so_Save_updates_it()
    {
        // RawMaterialUnitRow writes straight through into the stored RawMaterialUnit, and Save hands those very
        // entities to the repository — a unit that lost its id would be saved as a new one and the stored one deleted.
        var (viewModel, materialId) = await OpenPurchasedIngredientAsync();
        var savedUnitId = viewModel.UnitRows.Should().ContainSingle().Subject.Unit.Id;
        savedUnitId.Should().NotBe(0);

        Edit(viewModel, () => viewModel.UnitRows[0].BaseUnitsPerUnit = 50000m);
        viewModel.TryUndo().Should().BeTrue();

        viewModel.UnitRows[0].BaseUnitsPerUnit.Should().Be(25000m);
        viewModel.UnitRows[0].Unit.Id.Should().Be(savedUnitId, "an id of 0 would make Save add a replacement");

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        var unit = await dbContext.RawMaterialUnits.SingleAsync(u => u.RawMaterialId == materialId, Cancellation);
        unit.Id.Should().Be(savedUnitId, "the stored unit was updated, not deleted and recreated");
        unit.BaseUnitsPerUnit.Should().Be(25000m);
    }

    [Fact]
    public async Task Undoing_a_removed_alternative_unit_puts_it_back()
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();
        viewModel.SelectedUnitRow = viewModel.UnitRows[0];

        await viewModel.DeleteUnitCommand.Execute();
        viewModel.UnitRows.Should().BeEmpty();

        viewModel.TryUndo().Should().BeTrue();
        viewModel.UnitRows.Should().ContainSingle().Which.BaseUnitsPerUnit.Should().Be(25000m);
    }

    [Fact]
    public async Task Undoing_a_brand_price_puts_the_row_back_as_it_was()
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();

        Edit(viewModel, () =>
        {
            var row = viewModel.NewBrandPriceRow();
            row.Brand = "Baker's Best";
            row.UnitKey = viewModel.UnitRows[0].UnitOfMeasureId!.Value;
            row.Price = 1200m;
            viewModel.BrandPriceRows.Add(row);
        });
        viewModel.BrandPriceRows.Should().ContainSingle();

        viewModel.TryUndo().Should().BeTrue();
        viewModel.BrandPriceRows.Should().BeEmpty();

        viewModel.TryRedo().Should().BeTrue();
        var restored = viewModel.BrandPriceRows.Should().ContainSingle().Subject;
        restored.Brand.Should().Be("Baker's Best");
        restored.Price.Should().Be(1200m);
    }

    [Fact]
    public async Task Undo_leaves_what_Save_writes_alone()
    {
        var (viewModel, materialId) = await OpenPurchasedIngredientAsync();

        Edit(viewModel, () => viewModel.EditingName = "Renamed");
        viewModel.TryUndo().Should().BeTrue();

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(m => m.Id == materialId, Cancellation)).Name
            .Should().Be("Flour", "a change undone and a change never made come to the same thing");
    }

    [Fact]
    public async Task A_step_that_changes_nothing_is_not_recorded()
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();
        var recorder = (IUndoRecorder)viewModel;

        recorder.BeginUndoStep();
        recorder.EndUndoStep();
        viewModel.History.CanUndo.Should().BeFalse();

        Edit(viewModel, () => viewModel.EditingName = "Flour");
        viewModel.History.CanUndo.Should().BeFalse("it was retyped to the name it already had");
    }

    [Fact]
    public async Task The_history_goes_when_the_form_stops_being_the_one_it_recorded()
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();

        Edit(viewModel, () => viewModel.EditingName = "Renamed");
        viewModel.History.CanUndo.Should().BeTrue();

        // A Save reloads what was saved, which re-points the selection and so reloads the form.
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady, () => $"ready={viewModel.AreDetailsReady}");
        viewModel.History.CanUndo.Should().BeFalse("what's saved is the new starting point");

        Edit(viewModel, () => viewModel.EditingName = "Renamed again");
        viewModel.History.CanUndo.Should().BeTrue();

        await viewModel.NewCommand.Execute();
        viewModel.History.CanUndo.Should().BeFalse("a new material has no history of its own");
    }

    [Fact]
    public async Task A_step_taken_before_the_details_arrive_is_not_recorded()
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

        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.BrandPriceRows.Count == 1,
            () => $"ready={viewModel.AreDetailsReady}, brands={viewModel.BrandPriceRows.Count}");

        viewModel.History.CanUndo.Should().BeFalse("nothing typed before the details arrived is worth a step");
        viewModel.TryUndo().Should().BeFalse();
        viewModel.BrandPriceRows.Should().ContainSingle("the brand prices that arrived are still showing");
    }

    [Fact]
    public async Task Undoing_a_bom_edit_puts_the_headers_totals_back()
    {
        var (viewModel, _) = await OpenMadeIngredientAsync();
        viewModel.BomTotalWeight.Should().Be(970m, "1,000 g less the 3% weight loss");
        viewModel.BomTotalCost.Should().Be(110m, "1,000 g of sugar at its buffered ₱0.11");

        Edit(viewModel, () => viewModel.BomLineEdits.Single().Grams = 500m);
        viewModel.BomTotalWeight.Should().Be(485m);
        viewModel.BomTotalCost.Should().Be(55m);

        viewModel.TryUndo().Should().BeTrue();
        viewModel.BomTotalWeight.Should().Be(970m, "the restore works the totals out again");
        viewModel.BomTotalCost.Should().Be(110m);

        viewModel.TryRedo().Should().BeTrue();
        viewModel.BomTotalWeight.Should().Be(485m);
        viewModel.BomTotalCost.Should().Be(55m);
    }

    [Fact]
    public async Task Undoing_a_weight_loss_edit_puts_the_headers_yield_back()
    {
        var (viewModel, _) = await OpenMadeIngredientAsync();

        Edit(viewModel, () => viewModel.EditingWeightLossPercent = 20m);
        viewModel.BomTotalWeight.Should().Be(800m);
        viewModel.BomTotalCost.Should().Be(110m, "the weight loss doesn't move the cost");

        viewModel.TryUndo().Should().BeTrue();
        viewModel.EditingWeightLossPercent.Should().Be(3m);
        viewModel.BomTotalWeight.Should().Be(970m);
    }

    [Fact]
    public async Task Save_is_not_offered_until_the_form_is_changed()
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();

        viewModel.History.IsDirty.Should().BeFalse("the load itself isn't a change");
        (await CanSave(viewModel)).Should().BeFalse();

        Edit(viewModel, () => viewModel.EditingName = "Flour, Renamed");

        (await CanSave(viewModel)).Should().BeTrue();
    }

    [Fact]
    public async Task Save_stops_being_offered_once_the_change_is_taken_back()
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();
        Edit(viewModel, () => viewModel.UnitRows[0].BaseUnitsPerUnit = 50000m);
        (await CanSave(viewModel)).Should().BeTrue();

        viewModel.TryUndo().Should().BeTrue();

        (await CanSave(viewModel)).Should().BeFalse("the form shows what was saved again");
    }

    [Fact]
    public async Task Save_stops_being_offered_once_it_has_written()
    {
        var (viewModel, materialId) = await OpenPurchasedIngredientAsync();
        Edit(viewModel, () => viewModel.EditingName = "Flour, Renamed");

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.SelectedRawMaterial?.Id == materialId,
            () => $"ready={viewModel.AreDetailsReady}, selected={viewModel.SelectedRawMaterial?.Id}");

        (await CanSave(viewModel)).Should().BeFalse("what was written is the new starting point");
    }

    [Fact]
    public async Task Save_is_not_offered_on_a_blank_new_form()
    {
        var (viewModel, _) = await OpenPurchasedIngredientAsync();

        await viewModel.NewCommand.Execute();
        (await CanSave(viewModel)).Should().BeFalse("an empty form has nothing to write");

        Edit(viewModel, () => viewModel.EditingName = "Bread Flour");
        (await CanSave(viewModel)).Should().BeTrue();
    }

    [Fact]
    public async Task A_cell_still_open_keeps_Save_offered()
    {
        // What's typed into a cell doesn't reach the row until the edit ends, and the click that saves is the
        // click that ends it - a Save disabled under the cursor would swallow it.
        var (viewModel, _) = await OpenPurchasedIngredientAsync();
        var recorder = (IUndoRecorder)viewModel;

        recorder.IsEditPending = true;
        (await CanSave(viewModel)).Should().BeTrue();

        recorder.IsEditPending = false;
        (await CanSave(viewModel)).Should().BeFalse("the cell closed with nothing typed");
    }

    [Fact]
    public async Task Save_is_not_offered_on_the_blank_form_the_screen_opens_with()
    {
        await GetOrCreateUnitOfMeasureAsync("kilogram", "kg");

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        (await CanSave(viewModel)).Should().BeFalse("an empty form has nothing to write");
    }

    [Fact]
    public async Task Showing_the_tab_again_is_not_something_to_save()
    {
        // LoadCommand runs every time the tab is shown; it only merges in fresh data and re-points the pickers.
        var (viewModel, _) = await OpenPurchasedIngredientAsync();

        await viewModel.LoadCommand.Execute();

        (await CanSave(viewModel)).Should().BeFalse("a refresh isn't an edit");
    }

    /// <summary>Whether the Save button would be enabled: the command's own CanExecute, as the View binds it.</summary>
    private static async Task<bool> CanSave(RawMaterialsViewModel viewModel) =>
        await viewModel.SaveCommand.CanExecute.FirstAsync();

    /// <summary>One finished change, as the Controls/ behaviours bracket a gesture in the app.</summary>
    private static void Edit(RawMaterialsViewModel viewModel, Action change)
    {
        var recorder = (IUndoRecorder)viewModel;
        recorder.BeginUndoStep();
        change();
        recorder.EndUndoStep();
    }

    /// <summary>A saved purchased ingredient with one alternative unit (a 25 kg bag), open in the form.</summary>
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
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.UnitRows.Count == 1,
            () => $"ready={viewModel.AreDetailsReady}, units={viewModel.UnitRows.Count}");

        viewModel.History.CanUndo.Should().BeFalse("the load itself isn't a change");
        return (viewModel, materialId);
    }

    /// <summary>A saved made ingredient (3% weight loss) of 1,000 g of sugar at a buffered ₱0.11/g, open in the form.</summary>
    private async Task<(RawMaterialsViewModel ViewModel, int MaterialId)> OpenMadeIngredientAsync()
    {
        int materialId;
        await using (var dbContext = CreateDbContext())
        {
            var category = new RawMaterialCategory { Name = "Syrup goods" };
            dbContext.RawMaterialCategories.Add(category);
            var sugar = new RawMaterial
            {
                Name = "Sugar",
                Type = RawMaterialType.Ingredient,
                Category = category,
                Source = RawMaterialSource.Purchased,
                CostPerBaseUnit = 0.10m,
                BufferedCostPerBaseUnit = 0.11m,
            };
            var syrup = new RawMaterial
            {
                Name = "Simple Syrup",
                Type = RawMaterialType.Ingredient,
                Category = category,
                Source = RawMaterialSource.Made,
                WeightLossPercent = 3m,
            };
            dbContext.RawMaterials.AddRange(sugar, syrup);
            await dbContext.SaveChangesAsync(Cancellation);

            dbContext.IngredientBomLines.Add(new IngredientBomLine { IngredientId = syrup.Id, ComponentIngredientId = sugar.Id, Grams = 1000m });
            await dbContext.SaveChangesAsync(Cancellation);
            materialId = syrup.Id;
        }

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(m => m.Id == materialId);
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.BomLineEdits.Count == 1,
            () => $"ready={viewModel.AreDetailsReady}, lines={viewModel.BomLineEdits.Count}");

        viewModel.History.CanUndo.Should().BeFalse("the load itself isn't a change");
        return (viewModel, materialId);
    }

    private RawMaterialsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var costPropagator = CreateCostPropagator();

        return new RawMaterialsViewModel(
            new RawMaterialRepository(factory, costPropagator),
            new RawMaterialPriceHistoryRepository(factory),
            new RawMaterialBrandPriceRepository(factory, costPropagator),
            new IngredientBomLineRepository(factory, costPropagator),
            new RawMaterialCategoryRepository(factory),
            new RawMaterialCategoryValidator(),
            new RawMaterialValidator(),
            new RawMaterialUnitValidator(),
            new RawMaterialBrandPriceValidator(),
            new UnitOfMeasureRepository(factory),
            new LoggerConfiguration().CreateLogger());
    }

    private static async Task WaitUntilSettledAsync(Func<bool> condition, Func<string> describe)
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

        throw new TimeoutException($"The material never finished loading: {describe()}.");
    }
}
