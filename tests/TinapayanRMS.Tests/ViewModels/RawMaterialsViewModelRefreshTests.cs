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
/// Coming back to the Raw Materials tab re-runs LoadCommand (MainWindow). It must pick up what other
/// tabs changed while keeping the screen as the user left it: the selected row, unsaved form
/// edits and picker choices. Also covers the one Save that writes a material with its units, price and
/// BOM, the usage example, the empty form a new material starts with, and how its type decides which of
/// its fields apply.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class RawMaterialsViewModelRefreshTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task Reloading_keeps_the_selection_and_unsaved_edits_and_adds_a_unit_of_measure_created_on_another_tab()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single();
        viewModel.EditingName = "Flour (unsaved rename)";
        viewModel.CanAddUnit.Should().BeTrue("one alternative unit is below the limit of two");
        await viewModel.AddUnitCommand.Execute();
        viewModel.EditingUnitOfMeasure = viewModel.UnitsOfMeasure.Single();
        var selected = viewModel.SelectedRawMaterial;
        var pickedUnit = viewModel.EditingUnitOfMeasure;

        await SeedUnitOfMeasureAsync("sack", "sk");
        await viewModel.LoadCommand.Execute();

        // The grid and the picker track their selection by object, so the lists must still hold
        // the very objects that were selected, not fresh copies of them.
        viewModel.RawMaterials.Should().ContainSingle().Which.Should().BeSameAs(selected);
        viewModel.SelectedRawMaterial.Should().BeSameAs(selected);
        viewModel.UnitsOfMeasure.Should().Contain(unit => ReferenceEquals(unit, pickedUnit));
        viewModel.EditingUnitOfMeasure.Should().BeSameAs(pickedUnit);
        viewModel.IsUnitFormOpen.Should().BeTrue("the unsaved Alternative Units form stays open");
        viewModel.EditingName.Should().Be("Flour (unsaved rename)");
        viewModel.UnitsOfMeasure.Select(u => u.Name).Should().Equal("bag", "sack");
    }

    [Fact]
    public async Task Reloading_shows_a_unit_renamed_on_another_tab_without_reloading_the_form()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var flourId = await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single();
        viewModel.EditingName = "Flour (unsaved rename)";

        await RenameAbbreviationAsync(bagId, "bg");
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial!.Id.Should().Be(flourId);
        viewModel.SelectedRawMaterial.Units.Should().ContainSingle().Which.UnitOfMeasure!.Abbreviation.Should().Be("bg", "the row shows the renamed unit");
        viewModel.UnitRows.Should().ContainSingle().Which.Example.Should().Be("1 bg = 25,000 g");
        viewModel.EditingName.Should().Be("Flour (unsaved rename)", "re-pointing the selection must not reload the form");
    }

    [Fact]
    public async Task Unsaved_units_survive_a_reload_and_show_a_unit_renamed_on_another_tab()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Flour");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single();
        await WaitUntilSettledAsync(() => viewModel.EditingPurchaseUnit is not null,
            () => $"picked={viewModel.EditingPurchaseUnit?.Name ?? "none"}");
        await AddUnitAsync(viewModel, bagId, 25000m);
        viewModel.EditingPurchaseUnit = viewModel.PurchaseUnitOptions.Single(o => o.Name == "bag");

        await RenameAbbreviationAsync(bagId, "bg");
        await viewModel.LoadCommand.Execute();

        var row = viewModel.UnitRows.Should().ContainSingle().Subject;
        row.Unit.Id.Should().Be(0, "the unit still isn't saved");
        row.Example.Should().Be("1 bg = 25,000 g");
        viewModel.EditingPurchaseUnit!.Abbreviation.Should().Be("bg", "the purchase unit picked stays picked");
    }

    [Fact]
    public async Task One_save_adds_a_new_material_with_its_alternative_units_and_a_price_in_one_of_them()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Flour";
        PickCategory(viewModel);
        viewModel.ShowPurchaseSetup.Should().BeTrue("a new purchased material can be priced before it's saved");
        await AddUnitAsync(viewModel, bagId, 25000m);
        viewModel.EditingPurchaseUnit = viewModel.PurchaseUnitOptions.Single(o => o.Name == "bag");
        viewModel.EditingPrice = 1250m;

        // The second unit is left open in the form: Save applies it.
        await viewModel.AddUnitCommand.Execute();
        viewModel.EditingUnitOfMeasure = viewModel.UnitsOfMeasure.Single(u => u.Id == packId);
        viewModel.EditingUnitBaseUnitsPerUnit = 1000m;
        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.PriceHistory.Count == 1,
            () => $"price rows={viewModel.PriceHistory.Count}, error={viewModel.ErrorMessage ?? "none"}");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Flour was saved.", "a toast says so");
        viewModel.SelectedRawMaterial!.Name.Should().Be("Flour", "saving a new material selects it");
        viewModel.IsUnitFormOpen.Should().BeFalse();
        viewModel.UnitRows.Select(r => (r.UnitName, r.Unit.Id > 0)).Should().Equal(("bag", true), ("pack", true));
        viewModel.PriceHistory.Single().Entry.UnitId.Should().Be(viewModel.UnitRows[0].Unit.Id);
        viewModel.CurrentCostText.Should().Be("₱0.0500");
        viewModel.CanChangeTypeAndSource.Should().BeFalse();
    }

    [Fact]
    public async Task A_save_is_confirmed_until_the_next_action_and_confirmed_again_by_the_next_save()
    {
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Flour";
        PickCategory(viewModel);

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Flour was saved.");

        viewModel.EditingName = string.Empty;
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().Be("Name is required.");
        viewModel.SuccessMessage.Should().BeNull("an error replaces the confirmation");

        viewModel.EditingName = "Bread Flour";
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Bread Flour was saved.", "it names the material as saved");
    }

    [Fact]
    public async Task Picking_a_new_purchase_unit_adds_it_as_an_alternative_unit_and_one_save_saves_it_with_the_price()
    {
        await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedUnitOfMeasureAsync("sack", "sk");
        await SeedUnitOfMeasureAsync("gram", "g");
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Flour";
        PickCategory(viewModel);
        viewModel.PurchaseUnitOptions.Select(o => (o.Name, o.IsNew)).Should().Equal(
            [("gram", false), ("bag", true), ("sack", true)], "a unit of measure named like the ingredient's gram isn't offered twice");
        viewModel.EditingPurchaseUnit.Should().BeNull("a new material's purchase unit starts empty, like its other pickers");
        viewModel.IsPurchaseQuantityReadOnly.Should().BeTrue("there's no unit to type a quantity for");

        viewModel.EditingPurchaseUnit = viewModel.PurchaseUnitOptions.Single(o => o.Name == "sack");

        var row = viewModel.UnitRows.Should().ContainSingle().Subject;
        row.UnitName.Should().Be("sack");
        row.Unit.Id.Should().Be(0, "it isn't saved yet");
        viewModel.EditingPurchaseUnit!.Unit.Should().BeSameAs(row.Unit, "the pick is now the unit added");
        viewModel.EditingPurchaseUnit.IsNew.Should().BeFalse();
        viewModel.IsPurchaseQuantityReadOnly.Should().BeFalse();
        viewModel.EditingPurchaseQuantity.Should().BeNull("the unit has no quantity until it's typed");

        viewModel.EditingPurchaseQuantity = 25000m;
        viewModel.EditingPrice = 1500m;
        viewModel.UnitRows.Single().Example.Should().Be("1 sk = 25,000 g", "the quantity typed is the unit's multiplier");
        viewModel.PurchaseCostText.Should().Be("₱0.0600");

        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.PriceHistory.Count == 1,
            () => $"price rows={viewModel.PriceHistory.Count}, error={viewModel.ErrorMessage ?? "none"}");
        viewModel.ErrorMessage.Should().BeNull();
        var saved = viewModel.UnitRows.Should().ContainSingle().Subject;
        saved.Unit.Id.Should().BeGreaterThan(0);
        saved.BaseUnitsPerUnit.Should().Be(25000m);
        viewModel.PriceHistory.Single().Entry.UnitId.Should().Be(saved.Unit.Id);
        viewModel.EditingPurchaseUnit!.Name.Should().Be("sack", "the setup shows the latest price's unit");
        viewModel.IsPurchaseQuantityReadOnly.Should().BeTrue("a saved unit's multiplier changes with Edit in Alternative Units");
        viewModel.CurrentCostText.Should().Be("₱0.0600");
    }

    [Fact]
    public async Task Picking_another_purchase_unit_takes_back_the_one_added_there()
    {
        await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedUnitOfMeasureAsync("sack", "sk");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();

        viewModel.EditingPurchaseUnit = viewModel.PurchaseUnitOptions.Single(o => o.Name == "sack");
        viewModel.UnitRows.Select(r => r.UnitName).Should().Equal("sack");

        viewModel.EditingPurchaseUnit = viewModel.PurchaseUnitOptions.Single(o => o.Name == "bag");
        viewModel.UnitRows.Select(r => r.UnitName).Should().Equal(["bag"], "picking another new unit replaces the one added");
        viewModel.PurchaseUnitOptions.Select(o => (o.Name, o.IsNew)).Should().Equal(("gram", false), ("bag", false), ("sack", true));

        viewModel.EditingPurchaseUnit = viewModel.PurchaseUnitOptions.Single(o => o.Name == "gram");
        viewModel.UnitRows.Should().BeEmpty("the unit was only there as the purchase unit");
        viewModel.EditingPurchaseQuantity.Should().Be(1m);
    }

    [Fact]
    public async Task A_saved_units_quantity_is_read_only_and_new_units_are_offered_while_there_is_room_for_one()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedUnitOfMeasureAsync("pack", "pack");
        await SeedUnitOfMeasureAsync("sack", "sk");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single();
        await WaitUntilSettledAsync(() => viewModel.EditingPurchaseUnit?.Name == "bag",
            () => $"picked={viewModel.EditingPurchaseUnit?.Name ?? "none"}");
        viewModel.IsPurchaseQuantityReadOnly.Should().BeTrue("the bag unit is saved");
        viewModel.EditingPurchaseQuantity.Should().Be(25000m);
        viewModel.PurchaseUnitOptions.Where(o => o.IsNew).Select(o => o.Name).Should().Equal("pack", "sack");

        viewModel.EditingPurchaseUnit = viewModel.PurchaseUnitOptions.Single(o => o.Name == "pack");
        viewModel.UnitRows.Select(r => r.UnitName).Should().Equal("bag", "pack");
        viewModel.CanAddUnit.Should().BeFalse("that's the two alternative units allowed");
        viewModel.PurchaseUnitOptions.Where(o => o.IsNew).Select(o => o.Name).Should().Equal(["sack"], "the unit added here can still be swapped for another");

        viewModel.EditingPurchaseUnit = viewModel.PurchaseUnitOptions.Single(o => o.Name == "sack");
        viewModel.UnitRows.Select(r => r.UnitName).Should().Equal("bag", "sack");
    }

    [Fact]
    public async Task Saving_a_unit_added_as_the_purchase_unit_without_its_quantity_saves_nothing()
    {
        await SeedUnitOfMeasureAsync("sack", "sk");
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Flour";
        PickCategory(viewModel);
        viewModel.EditingPurchaseUnit = viewModel.PurchaseUnitOptions.Single(o => o.Name == "sack");
        viewModel.EditingPrice = 1500m;

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("Enter how many g are in one sack (Quantity per Purchase Unit).");
        viewModel.UnitRows.Should().ContainSingle().Which.UnitName.Should().Be("sack", "the edits stay to be fixed");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Leaving_a_purchased_material_while_on_price_history_goes_back_to_cost_setup()
    {
        await SeedPurchasedIngredientAsync("Flour");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single();
        viewModel.ShowPriceHistory.Should().BeTrue();
        viewModel.SelectedDetailsTab = RawMaterialsViewModel.PriceHistoryTab;

        await viewModel.NewCommand.Execute();

        viewModel.ShowPriceHistory.Should().BeFalse("a new material has no price history");
        viewModel.SelectedDetailsTab.Should().Be(0, "a hidden tab can't stay the one showing");
    }

    [Fact]
    public async Task A_rejected_save_saves_nothing_and_keeps_every_edit()
    {
        await SeedPurchasedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Flour"; // already taken
        PickCategory(viewModel);
        await AddUnitAsync(viewModel, bagId, 25000m);
        viewModel.EditingPurchaseUnit = viewModel.PurchaseUnitOptions.Single(o => o.Name == "bag");
        viewModel.EditingPrice = 1250m;

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("That name is already in use.");
        viewModel.SuccessMessage.Should().BeNull("nothing was saved");
        viewModel.SelectedRawMaterial.Should().BeNull();
        viewModel.CanChangeTypeAndSource.Should().BeTrue("the material still isn't saved");
        viewModel.UnitRows.Should().ContainSingle().Which.UnitName.Should().Be("bag");
        viewModel.EditingPurchaseUnit!.Name.Should().Be("bag");
        viewModel.EditingPrice.Should().Be(1250m);

        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.CountAsync(Cancellation)).Should().Be(1);
        (await dbContext.RawMaterialUnits.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.RawMaterialPriceHistories.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Saving_a_price_in_an_alternative_unit_records_the_unit_and_prices_the_ingredient_per_gram()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m), (packId, 1000m));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single();
        await WaitUntilSettledAsync(() => viewModel.PurchaseUnitOptions.Count == 3 && viewModel.EditingPurchaseUnit is not null,
            () => $"options={viewModel.PurchaseUnitOptions.Count}, picked={viewModel.EditingPurchaseUnit?.Name ?? "none"}");
        viewModel.EditingPurchaseUnit!.Name.Should().Be("bag", "an unpriced ingredient starts on its largest unit");
        viewModel.CanAddUnit.Should().BeFalse("the ingredient already has the two alternative units allowed");
        (await viewModel.AddUnitCommand.CanExecute.FirstAsync()).Should().BeFalse();

        viewModel.EditingPurchaseUnit = viewModel.PurchaseUnitOptions.Single(o => o.Name == "pack");
        viewModel.EditingPrice = 60m;
        viewModel.PurchaseCostText.Should().Be("₱0.0600", "the cost per gram is worked out as the price is typed");
        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.PriceHistory.Count == 1,
            () => $"price rows={viewModel.PriceHistory.Count}, error={viewModel.ErrorMessage ?? "none"}");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.PriceHistory.Single().UnitName.Should().Be("pack");
        viewModel.PriceHistory.Single().CostPerBaseUnit.Should().Be(0.06m);
        viewModel.SelectedRawMaterial!.CostPerBaseUnit.Should().Be(0.06m);
        viewModel.EditingPurchaseUnit!.Name.Should().Be("pack", "the setup shows the latest price's unit");
        viewModel.EditingPrice.Should().Be(60m);
    }

    [Fact]
    public async Task A_made_ingredients_bom_is_saved_with_the_rest_and_left_alone_by_a_save_that_did_not_touch_it()
    {
        await SeedPurchasedIngredientAsync("Sugar");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Simple Syrup";
        PickCategory(viewModel);
        viewModel.EditingSource = RawMaterialSource.Made;
        viewModel.EditingWeightLossPercent = 20m;
        viewModel.ShowPurchaseSetup.Should().BeFalse("a made ingredient has a BOM instead of a price");
        viewModel.BomLineEdits.Add(new IngredientLineRow { IngredientId = viewModel.Ingredients.Single().Id, Grams = 1000m });
        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.SelectedRawMaterial is not null && viewModel.BomLineEdits.Count == 1,
            () => $"selected={viewModel.SelectedRawMaterial?.Name ?? "none"}, lines={viewModel.BomLineEdits.Count}, error={viewModel.ErrorMessage ?? "none"}");
        viewModel.ErrorMessage.Should().BeNull();
        var lineId = await BomLineIdAsync();

        viewModel.EditingName = "Syrup";
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        (await BomLineIdAsync()).Should().Be(lineId, "an untouched BOM isn't sent, so its line isn't replaced");
    }

    [Fact]
    public async Task A_made_ingredient_left_at_the_default_weight_loss_saves_with_0_percent()
    {
        await SeedCategoryAsync("Syrups");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Simple Syrup";
        PickCategory(viewModel);
        viewModel.EditingWeightLossPercent.Should().BeNull("a purchased material has no weight loss");
        viewModel.EditingSource = RawMaterialSource.Made;
        viewModel.EditingWeightLossPercent.Should().Be(0m, "a made ingredient starts at 0%");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(Cancellation)).WeightLossPercent.Should().Be(0m);
    }

    [Fact]
    public async Task A_cleared_weight_loss_box_saves_as_0_percent()
    {
        await SeedCategoryAsync("Syrups");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Simple Syrup";
        PickCategory(viewModel);
        viewModel.EditingSource = RawMaterialSource.Made;
        viewModel.EditingWeightLossPercent = null; // the box shows 0.00 when it's empty

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(Cancellation)).WeightLossPercent.Should().Be(0m);
    }

    [Fact]
    public async Task New_after_an_unsaved_new_material_starts_with_no_units_price_or_bom_lines()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Sugar");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        await AddUnitAsync(viewModel, bagId, 25000m);
        viewModel.EditingPrice = 90m;
        viewModel.EditingSource = RawMaterialSource.Made;
        viewModel.BomLineEdits.Add(new IngredientLineRow { IngredientId = viewModel.Ingredients.Single().Id, Grams = 100m });

        await viewModel.NewCommand.Execute();

        viewModel.UnitRows.Should().BeEmpty();
        viewModel.CanAddUnit.Should().BeTrue();
        viewModel.PurchaseUnitOptions.Select(o => (o.Name, o.IsNew)).Should().Equal(("gram", false), ("bag", true));
        viewModel.EditingPrice.Should().BeNull();
        viewModel.BomLineEdits.Should().BeEmpty();
    }

    [Fact]
    public async Task The_usage_example_converts_the_quantity_to_grams_and_costs_it()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var flourId = await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var factory = CreateDbContextFactory();
        var bagUnitId = (await new RawMaterialUnitRepository(factory, CreateCostPropagator()).GetForRawMaterialAsync(flourId, Cancellation)).Single().Id;
        await new RawMaterialPriceHistoryRepository(factory, CreateCostPropagator()).AddAsync(
            new RawMaterialPriceHistory { RawMaterialId = flourId, UnitId = bagUnitId, Date = new DateOnly(2026, 9, 1), Price = 1250m },
            Cancellation); // ₱0.05/g

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.ExampleRawMaterial = viewModel.RawMaterials.Single();
        viewModel.ExampleQuantity = 2m;
        viewModel.ExampleUnit = viewModel.ExampleUnitOptions.Single(o => o.Name == "bag");

        await viewModel.CalculateExampleCommand.Execute();

        viewModel.ExampleResult.Should().NotBeNull();
        viewModel.ExampleResult!.Title.Should().Be("Cost for 2 bag of Flour");
        viewModel.ExampleResult.QuantityInBaseUnits.Should().Be(50000m);
        viewModel.ExampleResult.BaseUnitAbbreviation.Should().Be("g");
        viewModel.ExampleResult.CostPerBaseUnit.Should().Be(0.05m);
        viewModel.ExampleResult.TotalCost.Should().Be(2500m);
    }

    [Fact]
    public async Task A_cost_buffer_is_saved_with_the_material_and_shown_beside_the_true_cost()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingCostBufferPercent.Should().Be(0m, "a new material starts with no buffer");
        viewModel.EditingName = "Flour";
        PickCategory(viewModel);
        await AddUnitAsync(viewModel, bagId, 25000m);
        viewModel.EditingPurchaseUnit = viewModel.PurchaseUnitOptions.Single(o => o.Name == "bag");
        viewModel.EditingPrice = 1250m;
        viewModel.EditingCostBufferPercent = 5m;
        viewModel.PurchaseCostText.Should().Be("₱0.0500");
        viewModel.PurchaseBufferedCostText.Should().Be("Buffered (5%): ₱0.0525", "it follows the buffer as it's typed");

        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.PriceHistory.Count == 1,
            () => $"price rows={viewModel.PriceHistory.Count}, error={viewModel.ErrorMessage ?? "none"}");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SelectedRawMaterial!.CostBufferPercent.Should().Be(5m);
        viewModel.EditingCostBufferPercent.Should().Be(5m, "the form reloads what was saved");
        viewModel.CurrentCostText.Should().Be("₱0.0500");
        viewModel.BufferedCostText.Should().Be("₱0.0525");

        await viewModel.NewCommand.Execute();
        viewModel.EditingCostBufferPercent.Should().Be(0m);
    }

    [Fact]
    public async Task The_cost_buffer_percent_and_the_buffered_price_follow_each_other()
    {
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.IsBufferedPriceReadOnly.Should().BeTrue("the buffer percent is worked out from a price");
        viewModel.EditingBufferedPrice.Should().BeNull();

        viewModel.EditingPrice = 930m;
        viewModel.IsBufferedPriceReadOnly.Should().BeFalse();
        viewModel.EditingBufferedPrice.Should().Be(930m, "no buffer yet");

        viewModel.EditingCostBufferPercent = 5m;
        viewModel.EditingBufferedPrice.Should().Be(976.5m);

        viewModel.EditingBufferedPrice = 1023m;
        viewModel.EditingCostBufferPercent.Should().Be(10m);
        viewModel.EditingBufferedPrice.Should().Be(1023m, "what's typed stays as typed");

        viewModel.EditingPrice = 1000m;
        viewModel.EditingCostBufferPercent.Should().Be(10m, "the percent is what's saved, so a new price keeps it");
        viewModel.EditingBufferedPrice.Should().Be(1100m);
    }

    [Fact]
    public async Task Selecting_a_saved_material_shows_its_price_with_its_buffer_on_top()
    {
        var flourId = await SeedPurchasedIngredientAsync("Flour");
        await using (var dbContext = CreateDbContext())
        {
            var flour = await dbContext.RawMaterials.SingleAsync(m => m.Id == flourId, Cancellation);
            flour.CostBufferPercent = 5m;
            await dbContext.SaveChangesAsync(Cancellation);
        }
        await new RawMaterialPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator()).AddAsync(
            new RawMaterialPriceHistory { RawMaterialId = flourId, Date = new DateOnly(2026, 9, 1), Price = 930m },
            Cancellation);

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single();

        await WaitUntilSettledAsync(() => viewModel.EditingPrice == 930m,
            () => $"price={viewModel.EditingPrice?.ToString() ?? "none"}");
        viewModel.EditingCostBufferPercent.Should().Be(5m);
        viewModel.EditingBufferedPrice.Should().Be(976.5m);
    }

    [Fact]
    public async Task A_cleared_cost_buffer_box_saves_as_0_percent()
    {
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Sugar";
        PickCategory(viewModel);
        viewModel.EditingCostBufferPercent = null; // the box shows 0.00 when it's empty

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(Cancellation)).CostBufferPercent.Should().Be(0m);
    }

    [Fact]
    public async Task The_usage_example_shows_the_calculated_cost_the_buffer_and_the_buffered_cost()
    {
        var flourId = await SeedPurchasedIngredientAsync("Flour");
        await using (var dbContext = CreateDbContext())
        {
            var flour = await dbContext.RawMaterials.SingleAsync(m => m.Id == flourId, Cancellation);
            flour.CostBufferPercent = 5m;
            await dbContext.SaveChangesAsync(Cancellation);
        }
        await new RawMaterialPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator()).AddAsync(
            new RawMaterialPriceHistory { RawMaterialId = flourId, Date = new DateOnly(2026, 9, 1), Price = 0.05m },
            Cancellation); // ₱0.05/g, ₱0.0525/g buffered

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.ExampleRawMaterial = viewModel.RawMaterials.Single();
        viewModel.ExampleQuantity = 200m;
        viewModel.ExampleUnit = viewModel.ExampleUnitOptions.Single(o => o.Name == "gram");

        await viewModel.CalculateExampleCommand.Execute();

        viewModel.ExampleResult!.TotalCost.Should().Be(10m);
        viewModel.ExampleResult.BufferPercent.Should().Be(5m);
        viewModel.ExampleResult.BufferedTotalCost.Should().Be(10.5m);
    }

    [Fact]
    public async Task A_new_material_switched_to_packaging_drops_the_ingredient_only_fields_and_saves_its_own_base_unit()
    {
        await SeedPurchasedIngredientAsync("Flour");
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.IsEditingIngredient.Should().BeTrue("a new material starts as an ingredient");
        viewModel.EditingName = "Cake Box";
        PickCategory(viewModel);
        viewModel.EditingSource = RawMaterialSource.Made;
        viewModel.EditingWeightLossPercent = 5m;

        viewModel.EditingType = RawMaterialType.Packaging;
        viewModel.IsEditingPackaging.Should().BeTrue();
        viewModel.EditingSource.Should().Be(RawMaterialSource.Purchased, "packaging is always purchased");
        viewModel.EditingWeightLossPercent.Should().BeNull();
        viewModel.IsEditingMade.Should().BeFalse();

        viewModel.EditingBaseUnit = viewModel.UnitsOfMeasure.Single(u => u.Id == pieceId);
        viewModel.BaseUnitAbbreviation.Should().Be("pc", "the form names the base unit before the material is saved");
        viewModel.PurchaseUnitOptions.Select(o => o.Name).Should().Equal("piece");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        var box = viewModel.RawMaterials.Single(m => m.Name == "Cake Box");
        box.Type.Should().Be(RawMaterialType.Packaging);
        box.BaseUnitId.Should().Be(pieceId);
        viewModel.SelectedRawMaterial.Should().BeSameAs(box, "saving a new material selects it");
        viewModel.CurrentCostText.Should().Be("₱0.0000");
        viewModel.CanChangeTypeAndSource.Should().BeFalse("the type is fixed once saved");
        viewModel.Ingredients.Should().NotContain(box, "the BOM component picker lists ingredients only");
    }

    [Fact]
    public async Task New_starts_a_purchased_ingredient_with_the_other_pickers_empty_and_the_numbers_kept()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single();
        await WaitUntilSettledAsync(() => viewModel.EditingPurchaseUnit?.Name == "bag",
            () => $"picked={viewModel.EditingPurchaseUnit?.Name ?? "none"}");
        viewModel.ExampleRawMaterial.Should().NotBeNull("selecting a material puts it in the usage example");
        viewModel.EditingCostBufferPercent = 5m;
        viewModel.ExampleQuantity = 3m;

        await viewModel.NewCommand.Execute();

        viewModel.EditingName.Should().BeEmpty();
        viewModel.EditingType.Should().Be(RawMaterialType.Ingredient, "most materials are purchased ingredients");
        viewModel.EditingSource.Should().Be(RawMaterialSource.Purchased);
        viewModel.EditingCategory.Should().BeNull("the category isn't guessed");
        viewModel.EditingPurchaseUnit.Should().BeNull();
        viewModel.EditingPurchaseQuantity.Should().BeNull("there's no unit for it to be the quantity of");
        viewModel.ExampleRawMaterial.Should().BeNull();
        viewModel.ExampleUnit.Should().BeNull();
        viewModel.ExampleUnitOptions.Should().BeEmpty();

        viewModel.EditingPrice.Should().BeNull("the price box shows 0.00");
        viewModel.EditingCostBufferPercent.Should().Be(0m);
        viewModel.ExampleQuantity.Should().Be(3m, "the usage example's quantity stays");
        viewModel.ShowPurchaseSetup.Should().BeTrue("a new purchased material can be priced before it's saved");
        viewModel.BaseUnitAbbreviation.Should().Be("g");
    }

    [Fact]
    public async Task Saving_a_new_material_with_its_name_and_category_still_empty_saves_nothing()
    {
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("Name is required. Category is required.");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task A_price_typed_before_a_purchase_unit_is_picked_is_refused_rather_than_dropped()
    {
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Flour";
        PickCategory(viewModel);
        viewModel.EditingPrice = 1250m;

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("Pick the purchase unit the price is for.");
        viewModel.EditingPrice.Should().Be(1250m, "the edits stay to be fixed");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Deleting_a_material_leaves_an_empty_form()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var viewModel = CreateViewModel();
        viewModel.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single();
        await WaitUntilSettledAsync(() => viewModel.EditingPurchaseUnit?.Name == "bag",
            () => $"picked={viewModel.EditingPurchaseUnit?.Name ?? "none"}");

        await viewModel.DeleteCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.RawMaterials.Should().BeEmpty();
        viewModel.EditingName.Should().BeEmpty("the deleted material's details don't stay in the form");
        viewModel.EditingCategory.Should().BeNull();
        viewModel.EditingPurchaseUnit.Should().BeNull();
        viewModel.ExampleRawMaterial.Should().BeNull();
        viewModel.CanChangeTypeAndSource.Should().BeTrue("the form is a new material now");
    }

    [Fact]
    public async Task The_category_picker_lists_Add_New_above_the_categories_and_keeps_it_through_a_refresh()
    {
        await SeedCategoryAsync("Dry goods");
        await SeedCategoryAsync("Butter");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.CategoryOptions.Select(c => c.Name).Should().Equal("< Add New .. >", "Butter", "Dry goods");
        viewModel.Categories.Select(c => c.Name).Should().Equal(["Butter", "Dry goods"], "the row is the picker's, not a category");

        await viewModel.LoadCommand.Execute();

        viewModel.CategoryOptions[0].Should().BeSameAs(RawMaterialsViewModel.AddNewCategory, "the refresh merges categories, not the row");
    }

    [Fact]
    public async Task Picking_Add_New_saves_the_category_it_asks_for_and_files_the_material_under_it()
    {
        var viewModel = CreateViewModel();
        viewModel.PromptForNewCategory.RegisterHandler(interaction => interaction.SetOutput("Spices"));
        await viewModel.LoadCommand.Execute();

        viewModel.EditingCategory = RawMaterialsViewModel.AddNewCategory;

        await WaitUntilSettledAsync(() => viewModel.EditingCategory is not null,
            () => $"picked={viewModel.EditingCategory?.Name ?? "none"}, error={viewModel.ErrorMessage ?? "none"}");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Spices was added.", "a toast says so");
        viewModel.EditingCategory!.Name.Should().Be("Spices", "the material is filed under the category just added");
        viewModel.Categories.Select(c => c.Name).Should().Equal("Spices");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterialCategories.SingleAsync(Cancellation)).Name.Should().Be("Spices");
    }

    [Fact]
    public async Task Cancelling_the_new_category_prompt_leaves_the_category_that_was_picked()
    {
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        var prompted = false;
        viewModel.PromptForNewCategory.RegisterHandler(interaction =>
        {
            prompted = true;
            interaction.SetOutput(null);
        });
        await viewModel.LoadCommand.Execute();
        PickCategory(viewModel);

        viewModel.EditingCategory = RawMaterialsViewModel.AddNewCategory;

        await WaitUntilSettledAsync(() => prompted, () => "the prompt was never asked for");
        viewModel.EditingCategory!.Name.Should().Be("Dry goods", "the row was never a category to file it under");
        viewModel.Categories.Should().ContainSingle("nothing was added");
        viewModel.ErrorMessage.Should().BeNull();
    }

    private RawMaterialsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var costPropagator = CreateCostPropagator();

        return new RawMaterialsViewModel(
            new RawMaterialRepository(factory, costPropagator),
            new RawMaterialPriceHistoryRepository(factory, costPropagator),
            new IngredientBomLineRepository(factory, costPropagator),
            new RawMaterialCategoryRepository(factory),
            new RawMaterialCategoryValidator(),
            new RawMaterialValidator(),
            new RawMaterialUnitValidator(),
            new RawMaterialPriceHistoryValidator(),
            new UnitOfMeasureRepository(factory),
            new LoggerConfiguration().CreateLogger());
    }

    /// <summary>Adds an alternative unit through the form and applies it, which only puts it in the list.</summary>
    private static async Task AddUnitAsync(RawMaterialsViewModel viewModel, int unitOfMeasureId, decimal baseUnitsPerUnit)
    {
        await viewModel.AddUnitCommand.Execute();
        viewModel.EditingUnitOfMeasure = viewModel.UnitsOfMeasure.Single(u => u.Id == unitOfMeasureId);
        viewModel.EditingUnitBaseUnitsPerUnit = baseUnitsPerUnit;
        await viewModel.ApplyUnitCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
    }

    /// <summary>Picks the first category, which a new material starts without.</summary>
    private static void PickCategory(RawMaterialsViewModel viewModel) =>
        viewModel.EditingCategory = viewModel.Categories.First();

    private async Task SeedCategoryAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        dbContext.RawMaterialCategories.Add(new RawMaterialCategory { Name = name });
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private async Task<int> SeedPurchasedIngredientAsync(string name, params (int UnitOfMeasureId, decimal BaseUnitsPerUnit)[] units)
    {
        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = $"{name} goods" };
        dbContext.RawMaterialCategories.Add(category);
        var ingredient = new RawMaterial
        {
            Name = name,
            Type = RawMaterialType.Ingredient,
            Category = category,
            Source = RawMaterialSource.Purchased,
            Units = [.. units.Select(u => new RawMaterialUnit { UnitOfMeasureId = u.UnitOfMeasureId, BaseUnitsPerUnit = u.BaseUnitsPerUnit })],
        };
        dbContext.RawMaterials.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return ingredient.Id;
    }

    /// <summary>Renames a unit of measure's abbreviation the way the Settings dialog would, behind the screen's back.</summary>
    private async Task RenameAbbreviationAsync(int unitOfMeasureId, string abbreviation)
    {
        await using var dbContext = CreateDbContext();
        var unit = await dbContext.UnitsOfMeasure.FindAsync([unitOfMeasureId], Cancellation);
        unit!.Abbreviation = abbreviation;
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private async Task<int> BomLineIdAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.IngredientBomLines.Select(l => l.Id).SingleAsync(Cancellation);
    }

    /// <summary>
    /// Selecting a material loads its price history in the background (the ViewModel's selection
    /// subscriptions), with nothing to await. Waits until <paramref name="condition"/> has held for a
    /// moment, so a load still finishing can't change things after the test moves on.
    /// </summary>
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

        throw new TimeoutException($"The material's details never finished loading: {describe()}.");
    }
}
