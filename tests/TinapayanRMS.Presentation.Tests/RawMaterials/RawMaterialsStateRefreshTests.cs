using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Rules;
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
/// Coming back to the Raw Materials tab re-runs LoadCommand (ScreenBase). It must pick up what other
/// tabs changed while keeping the screen as the user left it: the selected row, unsaved form
/// edits and picker choices. Also covers the one Save that writes a material with its units, price and
/// BOM, the usage example, the empty form a new material starts with, and how its type decides which of
/// its fields apply.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class RawMaterialsStateRefreshTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task Reloading_keeps_the_selection_and_unsaved_edits_and_adds_a_unit_of_measure_created_on_another_tab() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await SelectAsync(state, state.RawMaterials.Single());
        state.EditingName = "Flour (unsaved rename)";
        var selected = state.SelectedRawMaterial;
        var pickedUnit = state.Units.Rows.Single().Unit.UnitOfMeasure;

        await SeedUnitOfMeasureAsync("sack", "sk");
        await state.LoadCommand.ExecuteAsync();

        // The grid and the picker track their selection by object, so the lists must still hold
        // the very objects that were selected, not fresh copies of them.
        state.RawMaterials.Should().ContainSingle().Which.Should().BeSameAs(selected);
        state.SelectedRawMaterial.Should().BeSameAs(selected);
        state.UnitsOfMeasure.Should().Contain(unit => ReferenceEquals(unit, pickedUnit));
        state.Units.Rows.Single().Unit.UnitOfMeasure.Should().BeSameAs(pickedUnit, "the grid's unit cell still finds the unit it shows");
        state.EditingName.Should().Be("Flour (unsaved rename)");
        state.UnitsOfMeasure.Select(u => u.Name).Should().Equal("bag", "sack");
    });

    [Test]
    public Task OpenRawMaterial_waits_for_a_list_not_loaded_yet_and_clears_a_search_hiding_it() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");
        var state = CreateState();

        // As the main window's Back does before the Raw Materials screen was loaded.
        state.OpenRecord(sugarId);
        state.SelectedRawMaterial.Should().BeNull("the list is empty until the screen loads");

        await state.LoadCommand.ExecuteAsync();

        state.SelectedRawMaterial!.Id.Should().Be(sugarId);
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.EditingName == "Sugar", () => $"name={state.EditingName}");

        state.List.SearchText = "Flour";
        state.OpenRecord(flourId);
        state.SelectedRawMaterial!.Id.Should().Be(flourId);
        state.List.SearchText.Should().Be("Flour", "a search showing it is left alone");

        state.OpenRecord(sugarId);
        state.SelectedRawMaterial!.Id.Should().Be(sugarId);
        state.List.SearchText.Should().BeEmpty("a search hiding it is cleared, so the list shows the row picked");
        state.List.VisibleRawMaterials.Should().Contain(state.SelectedRawMaterial);
        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => "Sugar's details never arrived");
    });

    [Test]
    public Task OpenByName_opens_the_material_a_name_means_best_as_the_Vim_command_line_asks() => UiThread.RunAsync(async () =>
    {
        await AddIngredientAsync("Brown Sugar");
        var sugarId = await AddIngredientAsync("Sugar");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.OpenByName("SUGAR").Should().BeTrue();

        state.SelectedRawMaterial!.Id.Should().Be(sugarId, "the name typed in full beats one that only holds it");
        state.OpenByName("salt").Should().BeFalse();
        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => "Sugar's details never arrived");
    });

    [Test]
    public Task Reloading_shows_a_unit_renamed_on_another_tab_without_reloading_the_form() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var flourId = await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await SelectAsync(state, state.RawMaterials.Single());
        state.EditingName = "Flour (unsaved rename)";

        await RenameAbbreviationAsync(bagId, "bg");
        await state.LoadCommand.ExecuteAsync();

        state.SelectedRawMaterial!.Id.Should().Be(flourId);
        state.SelectedRawMaterial.Units.Should().ContainSingle().Which.UnitOfMeasure!.Abbreviation.Should().Be("bg", "the row shows the renamed unit");
        var row = state.Units.Rows.Should().ContainSingle().Subject;
        (row.Abbreviation, row.BaseEquivalentText).Should().Be(("bg", "25,000 g"));
        state.EditingName.Should().Be("Flour (unsaved rename)", "re-pointing the selection must not reload the form");
    });


    [Test]
    public Task A_second_pick_before_the_first_ones_details_arrive_shows_only_its_own() => UiThread.RunAsync(async () =>
    {
        var flourId = await SeedPurchasedIngredientAsync("Flour");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        await CreateBrandPriceRepository().SetAsync(
            flourId,
            [new RawMaterialBrandPrice { Brand = "Gold Medal", Price = 0.05m }, new RawMaterialBrandPrice { Brand = "Wellington", Price = 0.04m }],
            Cancellation);
        await SetPriceAsync(sugarId, null, 0.09m);
        await SeedMadeIngredientAsync("Butter Icing", (flourId, 100m), (sugarId, 50m));
        await SeedMadeIngredientAsync("Syrup", (sugarId, 100m));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        // Sugar picked before Flour's details can arrive, as a quick second click or an arrow key held down in the list does.
        state.SelectedRawMaterial = state.RawMaterials.Single(m => m.Id == flourId);
        state.SelectedRawMaterial = state.RawMaterials.Single(m => m.Id == sugarId);

        await Settle.UntilAsync(state, () => state.PurchaseCost.BrandPriceRows.Count > 0 && state.PriceLog.Entries.Count > 0,
            () => $"brands={state.PurchaseCost.BrandPriceRows.Count}, prices={state.PriceLog.Entries.Count}");
        state.PurchaseCost.BrandPriceRows.Select(r => (r.Brand, r.Price)).Should().Equal([(null, 0.09m)], "Flour's brands never join Sugar's");
        state.PriceLog.Entries.Should().ContainSingle().Which.Entry.RawMaterialId.Should().Be(sugarId);

        // The same for the recipe a made ingredient is made from.
        state.SelectedRawMaterial = state.RawMaterials.Single(m => m.Name == "Butter Icing");
        state.SelectedRawMaterial = state.RawMaterials.Single(m => m.Name == "Syrup");

        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.Lines.Count == 1, () => $"lines={state.Recipe.Lines.Count}");
        state.Recipe.Lines.Select(l => (l.Ingredient!.Name, l.Grams)).Should().Equal([("Sugar", 100m)], "Butter Icing's lines never join Syrup's");
    });

    [Test]
    public Task Picking_another_material_keeps_the_previous_details_until_its_own_arrive_and_Save_waits_for_them() => UiThread.RunAsync(async () =>
    {
        var flourId = await SeedPurchasedIngredientAsync("Flour");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        await CreateBrandPriceRepository().SetAsync(
            flourId,
            [new RawMaterialBrandPrice { Brand = "Gold Medal", Price = 0.05m }, new RawMaterialBrandPrice { Brand = "Wellington", Price = 0.04m }],
            Cancellation);
        await SetPriceAsync(sugarId, null, 0.09m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await SelectAsync(state, state.RawMaterials.Single(m => m.Id == flourId));

        state.SelectedRawMaterial = state.RawMaterials.Single(m => m.Id == sugarId);

        // Its details are still on their way (read off the UI thread): Flour's show until they arrive, not an empty grid,
        // and a Save now would write Flour's brand prices under Sugar.
        state.AreDetailsReady.Should().BeFalse();
        state.EditingName.Should().Be("Sugar");
        state.PurchaseCost.BrandPriceRows.Select(r => r.Brand).Should().Equal("Gold Medal", "Wellington");
        await state.SaveCommand.ExecuteAsync();

        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.PurchaseCost.BrandPriceRows.Count == 1,
            () => $"ready={state.AreDetailsReady}, brands={state.PurchaseCost.BrandPriceRows.Count}");
        state.PurchaseCost.BrandPriceRows.Single().Price.Should().Be(0.09m);
        state.SuccessMessage.Should().BeNull("nothing was saved");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterialBrandPrices.Where(p => p.RawMaterialId == sugarId).Select(p => p.Price).ToListAsync(Cancellation))
            .Should().Equal([0.09m], "the Save did nothing");
    });

    [Test]
    public Task Unsaved_units_survive_a_reload_and_show_a_unit_renamed_on_another_tab() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Flour");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await SelectAsync(state, state.RawMaterials.Single());
        await AddUnitAsync(state, bagId, 25000m);
        var brand = AddBrandPrice(state, "Gold Medal", "bag", 1250m);

        await RenameAbbreviationAsync(bagId, "bg");
        await state.LoadCommand.ExecuteAsync();

        var row = state.Units.Rows.Should().ContainSingle().Subject;
        row.Unit.Id.Should().Be(0, "the unit still isn't saved");
        (row.Abbreviation, row.BaseEquivalentText).Should().Be(("bg", "25,000 g"));
        state.PurchaseCost.BrandPriceRows.Should().ContainSingle().Which.Should().BeSameAs(brand, "the unsaved brand stays");
        state.PurchaseCost.PurchaseUnitOptions.Single(o => o.Key == brand.UnitKey).Abbreviation.Should().Be("bg", "the unit picked stays picked");
    });


    [Test]
    public Task One_save_adds_a_new_material_with_its_alternative_units_and_a_price_in_one_of_them() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var trayId = await SeedUnitOfMeasureAsync("tray", "tray");
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Flour";
        PickCategory(state);
        state.ShowPurchaseSetup.Should().BeTrue("a new purchased material can be priced before it's saved");
        await AddUnitAsync(state, bagId, 25000m);
        AddBrandPrice(state, "Gold Medal", "bag", 1250m);

        await AddUnitAsync(state, packId, 1000m);
        await AddUnitAsync(state, trayId, 500m);
        await state.SaveCommand.ExecuteAsync();

        await Settle.UntilAsync(state, () => state.PriceLog.Entries.Count == 1 && state.PurchaseCost.BrandPriceRows.Count == 1,
            () => $"price rows={state.PriceLog.Entries.Count}, brands={state.PurchaseCost.BrandPriceRows.Count}, error={state.ErrorMessage ?? "none"}");
        state.ErrorMessage.Should().BeNull();
        state.SuccessMessage.Should().Be("Flour was saved.", "a toast says so");
        state.SelectedRawMaterial!.Name.Should().Be("Flour", "saving a new material selects it");
        state.Units.Rows.Select(r => (r.UnitName, r.Unit.Id > 0)).Should().Equal(
            [("bag", true), ("pack", true), ("tray", true)], "a material can have any number of alternative units");
        state.PriceLog.Entries.Single().Entry.UnitId.Should().Be(state.Units.Rows[0].Unit.Id);
        state.PriceLog.Entries.Single().Brand.Should().Be("Gold Medal");
        var brand = state.PurchaseCost.BrandPriceRows.Single();
        (brand.Brand, brand.UnitKey, brand.Price).Should().Be(("Gold Medal", bagId, 1250m), "the form reloads what was saved");
        state.PurchaseCost.HeaderCostUnitText.Should().Be("bag", "the header's costs are in the Per unit, the dearest brand's");
        state.PurchaseCost.CurrentCostText.Should().Be("1,250.00");
        state.CanChangeTypeAndSource.Should().BeFalse();
    });

    [Test]
    public Task A_save_is_confirmed_until_the_next_action_and_confirmed_again_by_the_next_save() => UiThread.RunAsync(async () =>
    {
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Flour";
        PickCategory(state);

        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        state.SuccessMessage.Should().Be("Flour was saved.");

        state.EditingName = string.Empty;
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().Be("Every raw material needs a name.");
        state.SuccessMessage.Should().BeNull("an error replaces the confirmation");

        state.EditingName = "Bread Flour";
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        state.SuccessMessage.Should().Be("Bread Flour was saved.", "it names the material as saved");
    });


    [Test]
    public Task Picking_a_new_purchase_unit_adds_it_as_an_alternative_unit_and_one_save_saves_it_with_the_price() => UiThread.RunAsync(async () =>
    {
        await SeedUnitOfMeasureAsync("bag", "bag");
        var sackId = await SeedUnitOfMeasureAsync("sack", "sk");
        await SeedUnitOfMeasureAsync("gram", "g");
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Flour";
        PickCategory(state);
        state.PurchaseCost.PurchaseUnitOptions.Select(o => o.Name).Should().Equal(
            ["gram", "bag", "sack"], "a unit of measure named like the ingredient's gram isn't offered twice");

        // The grid's add row edits the screen's own row, which adds the unit as soon as it's picked.
        var brand = state.PurchaseCost.NewBrandPriceRow();
        brand.CanTypeQuantity.Should().BeFalse("there's no unit to type a quantity for");
        brand.UnitKey = sackId;

        var row = state.Units.Rows.Should().ContainSingle().Subject;
        row.UnitName.Should().Be("sack");
        row.Unit.Id.Should().Be(0, "it isn't saved yet");
        brand.CanTypeQuantity.Should().BeTrue();
        brand.Quantity.Should().BeNull("the unit has no quantity until it's typed");

        brand.Quantity = 25000m;
        brand.Price = 1500m;
        state.PurchaseCost.BrandPriceRows.Add(brand);
        state.Units.Rows.Single().BaseEquivalentText.Should().Be("25,000 g", "the quantity typed is the unit's multiplier");
        state.PurchaseCost.PurchaseCostText.Should().Be("0.0600");

        await state.SaveCommand.ExecuteAsync();

        await Settle.UntilAsync(state, () => state.PriceLog.Entries.Count == 1 && state.PurchaseCost.BrandPriceRows.Count == 1,
            () => $"price rows={state.PriceLog.Entries.Count}, brands={state.PurchaseCost.BrandPriceRows.Count}, error={state.ErrorMessage ?? "none"}");
        state.ErrorMessage.Should().BeNull();
        var saved = state.Units.Rows.Should().ContainSingle().Subject;
        saved.Unit.Id.Should().BeGreaterThan(0);
        saved.BaseUnitsPerUnit.Should().Be(25000m);
        state.PriceLog.Entries.Single().Entry.UnitId.Should().Be(saved.Unit.Id);
        var reloaded = state.PurchaseCost.BrandPriceRows.Single();
        (reloaded.UnitKey, reloaded.Quantity).Should().Be((sackId, 25000m));
        reloaded.CanTypeQuantity.Should().BeFalse("a saved unit's multiplier is corrected in Alternative Units");
        state.PurchaseCost.CurrentCostText.Should().Be("1,500.00", "a 25 kg sack's worth, the Per unit");
    });


    [Test]
    public Task Picking_another_purchase_unit_takes_back_the_one_added_there() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var sackId = await SeedUnitOfMeasureAsync("sack", "sk");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();

        var brand = state.PurchaseCost.NewBrandPriceRow();
        brand.UnitKey = sackId;
        state.Units.Rows.Select(r => r.UnitName).Should().Equal("sack");

        brand.UnitKey = bagId;
        state.Units.Rows.Select(r => r.UnitName).Should().Equal(["bag"], "picking another new unit replaces the one added");

        brand.UnitKey = PurchaseUnitChoice.BaseUnitKey;
        state.Units.Rows.Should().BeEmpty("the unit was only there for the brand");
        brand.Quantity.Should().Be(1m);
    });


    [Test]
    public Task A_saved_units_quantity_is_read_only_and_each_new_unit_picked_on_a_brand_is_added() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var sackId = await SeedUnitOfMeasureAsync("sack", "sk");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await SelectAsync(state, state.RawMaterials.Single());
        var bag = AddBrandPrice(state, "Gold Medal", "bag", 1250m);
        bag.CanTypeQuantity.Should().BeFalse("the bag unit is saved");
        bag.Quantity.Should().Be(25000m);

        var pack = AddBrandPrice(state, "Wellington", "pack", 40m);
        state.Units.Rows.Select(r => r.UnitName).Should().Equal("bag", "pack");

        var sack = state.PurchaseCost.NewBrandPriceRow();
        sack.UnitKey = sackId;
        state.ErrorMessage.Should().BeNull();
        state.Units.Rows.Select(r => r.UnitName).Should().Equal(["bag", "pack", "sack"], "there's no limit on alternative units");

        sack.UnitKey = PurchaseUnitChoice.BaseUnitKey;
        pack.UnitKey = sackId;
        state.Units.Rows.Select(r => r.UnitName).Should().Equal(["bag", "sack"], "the unit added for a brand can still be swapped for another");
    });


    [Test]
    public Task Saving_a_unit_added_for_a_brand_without_its_quantity_saves_nothing() => UiThread.RunAsync(async () =>
    {
        await SeedUnitOfMeasureAsync("sack", "sk");
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Flour";
        PickCategory(state);
        AddBrandPrice(state, null, "sack", 1500m);

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("Enter how many g are in one sack (Qty per Purchase Unit).");
        state.Units.Rows.Should().ContainSingle().Which.UnitName.Should().Be("sack", "the edits stay to be fixed");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
    });

    [Test]
    public Task Leaving_a_purchased_material_while_on_price_history_goes_back_to_cost_setup() => UiThread.RunAsync(async () =>
    {
        await SeedPurchasedIngredientAsync("Flour");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await SelectAsync(state, state.RawMaterials.Single());
        state.ShowPriceHistory.Should().BeTrue();
        state.SelectedDetailsTab = RawMaterialsState.PriceHistoryTab;

        await state.NewCommand.ExecuteAsync();

        state.ShowPriceHistory.Should().BeFalse("a new material has no price history");
        state.SelectedDetailsTab.Should().Be(0, "a hidden tab can't stay the one showing");
    });

    [Test]
    public Task Used_By_lists_the_picked_materials_products_follows_other_tabs_and_opens_one_on_a_tile() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [(flourId, 1000m)]);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await SelectAsync(state, state.RawMaterials.Single(m => m.Id == flourId));
        state.Dependents.Products.Select(p => p.Name).Should().Equal("Pandesal");
        state.Dependents.Text.Should().StartWith("These products use it");

        // A product made on the Products tab while this screen kept Flour open.
        await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m), (sugarId, 100m)]);
        await state.LoadCommand.ExecuteAsync();
        state.Dependents.Products.Select(p => p.Name).Should().Equal("Ensaymada", "Pandesal");

        var opened = new List<int>();
        using (state.Dependents.ProductOpenRequests.Subscribe(opened.Add))
        {
            await state.Dependents.OpenProductCommand.ExecuteAsync(pandesalId);
        }

        opened.Should().Equal(pandesalId);

        await state.NewCommand.ExecuteAsync();
        state.Dependents.Products.Should().BeEmpty("a new material isn't used by anything");
        state.Dependents.Text.Should().Be("No product uses it yet.");
    });

    [Test]
    public Task A_rejected_save_saves_nothing_and_keeps_every_edit() => UiThread.RunAsync(async () =>
    {
        await SeedPurchasedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Flour"; // already taken
        PickCategory(state);
        await AddUnitAsync(state, bagId, 25000m);
        AddBrandPrice(state, "Gold Medal", "bag", 1250m);

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("That name is already in use.");
        state.SuccessMessage.Should().BeNull("nothing was saved");
        state.SelectedRawMaterial.Should().BeNull();
        state.CanChangeTypeAndSource.Should().BeTrue("the material still isn't saved");
        state.Units.Rows.Should().ContainSingle().Which.UnitName.Should().Be("bag");
        state.PurchaseCost.BrandPriceRows.Should().ContainSingle().Which.Price.Should().Be(1250m);

        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.CountAsync(Cancellation)).Should().Be(1);
        (await dbContext.RawMaterialUnits.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.RawMaterialBrandPrices.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.RawMaterialPriceHistories.AnyAsync(Cancellation)).Should().BeFalse();
    });

    [Test]
    public Task A_name_with_spaces_round_it_is_saved_and_shown_without_them() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await SeedCategoryAsync("Sugars");
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = " Brown Sugar ";
        PickCategory(state);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.EditingName.Should().Be("Brown Sugar");
        state.SuccessMessage.Should().Be("Brown Sugar was saved.");
        state.History.IsDirty.Should().BeFalse("what's showing is what was saved");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(Cancellation)).Name.Should().Be("Brown Sugar");
    });

    [Test]
    public Task A_name_taken_but_for_a_space_at_the_end_is_refused_and_not_saved_beside_it() => UiThread.RunAsync(async () =>
    {
        await SeedPurchasedIngredientAsync("Flour");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Flour ";
        PickCategory(state);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("That name is already in use.");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.CountAsync(Cancellation)).Should().Be(1);
    });

    [Test]
    public Task Leaving_the_name_field_flags_a_name_already_taken() => UiThread.RunAsync(async () =>
    {
        await SeedPurchasedIngredientAsync("Flour");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "  flour  "; // different case, padded — still the same name

        state.CheckNameAvailability();

        state.ErrorMessage.Should().Be("That name is already in use.");
    });

    [Test]
    public Task Leaving_the_name_field_does_not_flag_the_materials_own_name_and_clears_once_fixed() => UiThread.RunAsync(async () =>
    {
        await SeedPurchasedIngredientAsync("Flour");
        await SeedPurchasedIngredientAsync("Sugar");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        // Selecting Flour and leaving its name untouched isn't a conflict with itself.
        await SelectAsync(state, state.RawMaterials.Single(m => m.Name == "Flour"));
        state.CheckNameAvailability();
        state.ErrorMessage.Should().BeNull();

        // Renaming it to another material's name is flagged...
        state.EditingName = "Sugar";
        state.CheckNameAvailability();
        state.ErrorMessage.Should().Be("That name is already in use.");

        // ...and fixing it clears the message this check raised.
        state.EditingName = "Cake Flour";
        state.CheckNameAvailability();
        state.ErrorMessage.Should().BeNull("the name is free again, so the message it raised clears itself");
    });


    [Test]
    public Task Saving_a_price_in_an_alternative_unit_records_the_unit_and_prices_the_ingredient_per_gram() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m), (packId, 1000m));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await SelectAsync(state, state.RawMaterials.Single());
        AddBrandPrice(state, null, "pack", 60m);
        state.PurchaseCost.PurchaseCostText.Should().Be("0.0600", "the cost per gram is worked out as the price is typed");
        await state.SaveCommand.ExecuteAsync();

        await Settle.UntilAsync(state, () => state.PriceLog.Entries.Count == 1 && state.PurchaseCost.BrandPriceRows.Count == 1,
            () => $"price rows={state.PriceLog.Entries.Count}, brands={state.PurchaseCost.BrandPriceRows.Count}, error={state.ErrorMessage ?? "none"}");
        state.ErrorMessage.Should().BeNull();
        state.PriceLog.Entries.Single().UnitName.Should().Be("pack");
        state.PriceLog.Entries.Single().CostPerBaseUnit.Should().Be(0.06m);
        state.SelectedRawMaterial!.CostPerBaseUnit.Should().Be(0.06m);
        (state.PurchaseCost.BrandPriceRows.Single().UnitKey, state.PurchaseCost.BrandPriceRows.Single().Price).Should().Be((packId, 60m));
    });

    [Test]
    public Task The_material_is_costed_at_the_dearest_brand_and_the_summary_says_which() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m), (packId, 1000m));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await SelectAsync(state, state.RawMaterials.Single());
        AddBrandPrice(state, "Gold Medal", "bag", 900m); // ₱0.036/g
        AddBrandPrice(state, "Wellington", "pack", 40m); // ₱0.04/g
        state.PurchaseCost.PurchaseCostText.Should().Be("0.0400");
        state.PurchaseCost.PurchaseCostSourceText.Should().Be("From Wellington, the dearest of 2 brands.");

        await state.SaveCommand.ExecuteAsync();

        await Settle.UntilAsync(state, () => state.PriceLog.Entries.Count == 1 && state.PurchaseCost.BrandPriceRows.Count == 2,
            () => $"price rows={state.PriceLog.Entries.Count}, brands={state.PurchaseCost.BrandPriceRows.Count}, error={state.ErrorMessage ?? "none"}");
        state.ErrorMessage.Should().BeNull();
        state.SelectedRawMaterial!.CostPerBaseUnit.Should().Be(0.04m);
        (state.PriceLog.Entries.Single().Brand, state.PriceLog.Entries.Single().UnitName).Should().Be(("Wellington", "pack"));
    });

    [Test]
    public Task Removing_a_brand_takes_back_the_unit_added_for_it() => UiThread.RunAsync(async () =>
    {
        await SeedUnitOfMeasureAsync("sack", "sk");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();

        var brand = AddBrandPrice(state, "Gold Medal", "sack", 1500m);
        state.Units.Rows.Select(r => r.UnitName).Should().Equal("sack");

        state.PurchaseCost.SelectedBrandPriceRow = brand;
        await state.PurchaseCost.RemoveBrandPriceCommand.ExecuteAsync();

        state.PurchaseCost.BrandPriceRows.Should().BeEmpty();
        state.Units.Rows.Should().BeEmpty("the unit was only there for the brand");
        state.PurchaseCost.PurchaseCostText.Should().Be("—");
    });

    [Test]
    public Task Save_refuses_a_brand_priced_twice_in_the_same_unit() => UiThread.RunAsync(async () =>
    {
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Sugar";
        PickCategory(state);
        AddBrandPrice(state, "Bounty", "gram", 0.09m);
        AddBrandPrice(state, "bounty ", "gram", 0.1m);

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("'Bounty' is priced twice in the same unit. Remove one.");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
    });

    [Test]
    public Task A_made_ingredients_sub_assembly_and_add_ins_are_saved_with_the_rest_and_add_ins_left_alone_by_a_save_that_did_not_touch_them() => UiThread.RunAsync(async () =>
    {
        await MadeCategoryIdAsync();
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        await SeedPurchasedIngredientAsync("Vanilla");
        var recipeId = await AddRecipeAsync("Syrup Base", [(sugarId, 1000m)]);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Simple Syrup";
        state.EditingSource = RawMaterialSource.Made;
        PickCategory(state);
        state.EditingWeightLossPercent = 20m;
        state.ShowPurchaseSetup.Should().BeFalse("a made ingredient is made from a sub-assembly instead of priced");
        await PickRecipeAsync(state, "Syrup Base");
        state.Recipe.AddIns.Add(new IngredientLineRow { IngredientId = state.Ingredients.Single(m => m.Name == "Vanilla").Id, Grams = 10m });
        await state.SaveCommand.ExecuteAsync();

        await Settle.UntilAsync(state, () => state.SelectedRawMaterial is not null && state.Recipe.AddIns.Count == 1,
            () => $"selected={state.SelectedRawMaterial?.Name ?? "none"}, lines={state.Recipe.AddIns.Count}, error={state.ErrorMessage ?? "none"}");
        state.ErrorMessage.Should().BeNull();
        state.SelectedRawMaterial!.SubAssemblyId.Should().Be(recipeId);
        state.PurchaseCost.HeaderCostUnitText.Should().Be("g", "a made ingredient has no Purchase Cost Setup, so its header stays per gram");
        state.PurchaseCost.CurrentCostText.Should().MatchRegex(@"^\d+\.\d{4}$");
        var addInId = await AddInIdAsync();

        state.EditingName = "Syrup";
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await AddInIdAsync()).Should().Be(addInId, "untouched add-ins aren't sent, so the line isn't replaced");
    });

    [Test]
    public Task A_made_ingredient_needs_a_sub_assembly_picked_before_it_saves() => UiThread.RunAsync(async () =>
    {
        await MadeCategoryIdAsync();
        await SeedPurchasedIngredientAsync("Sugar");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Simple Syrup";
        state.EditingSource = RawMaterialSource.Made;
        PickCategory(state);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("Pick the sub-assembly it's made from.");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(m => m.Name == "Simple Syrup", Cancellation)).Should().BeFalse();
    });

    [Test]
    public Task The_recipe_card_lists_only_recipes_and_opens_the_picked_one_on_Sub_Assemblies() => UiThread.RunAsync(async () =>
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        var recipeId = await AddRecipeAsync("Syrup Base", [(sugarId, 1000m)]);
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 9, isPackaging: true);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.SubAssemblies.Add(new SubAssembly { Name = "Gift Set", RoleId = packagingRoleId });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Recipe.SubAssemblyOptions.Select(s => s.Name).Should().Equal(["Syrup Base"], "a packaging set isn't a recipe");

        var opened = new List<int>();
        using var subscription = state.Recipe.SubAssemblyOpenRequests.Subscribe(opened.Add);
        state.EditingSource = RawMaterialSource.Made;
        await PickRecipeAsync(state, "Syrup Base");
        await state.Recipe.OpenSubAssemblyCommand.ExecuteAsync();

        opened.Should().Equal(recipeId);
    });

    [Test]
    public Task A_picked_sub_assembly_shows_as_a_badge_and_only_Edit_brings_the_picker_back() => UiThread.RunAsync(async () =>
    {
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        await AddRecipeAsync("Syrup Base", [(sugarId, 1000m)]);
        await AddRecipeAsync("Thin Syrup Base", [(sugarId, 500m)]);
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup", (sugarId, 800m));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingSource = RawMaterialSource.Made;
        state.Recipe.IsPickingSubAssembly.Should().BeTrue("nothing is picked yet");
        await PickRecipeAsync(state, "Syrup Base");
        (state.Recipe.IsPickingSubAssembly, state.Recipe.IsShowingSubAssemblyBadge).Should().Be((false, true), "a pick settles it");
        state.Recipe.SubAssemblyBadgeText.Should().Be("Syrup Base, from Sub-Assemblies");

        await state.Recipe.StartChangingSubAssemblyCommand.ExecuteAsync();
        (state.Recipe.IsPickingSubAssembly, state.Recipe.IsChangingSubAssembly).Should().Be((true, true));
        await state.Recipe.CancelChangingSubAssemblyCommand.ExecuteAsync();
        state.Recipe.IsShowingSubAssemblyBadge.Should().BeTrue();
        state.Recipe.SubAssembly!.Name.Should().Be("Syrup Base", "Cancel keeps it as it was");

        await state.Recipe.StartChangingSubAssemblyCommand.ExecuteAsync();
        state.Recipe.SubAssembly = state.Recipe.SubAssemblyOptions.Single(s => s.Name == "Thin Syrup Base");
        (state.Recipe.IsChangingSubAssembly, state.Recipe.IsShowingSubAssemblyBadge).Should().Be((false, true), "another pick settles it again");

        // A saved one opens with its badge, even after Edit was left open on the form before.
        await state.Recipe.StartChangingSubAssemblyCommand.ExecuteAsync();
        state.SelectedRawMaterial = state.RawMaterials.Single(m => m.Id == syrupId);
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.Lines.Count == 1, () => $"lines={state.Recipe.Lines.Count}");
        (state.Recipe.IsChangingSubAssembly, state.Recipe.IsShowingSubAssemblyBadge).Should().Be((false, true));
        state.Recipe.SubAssemblyBadgeText.Should().Be("Simple Syrup Base, from Sub-Assemblies");
    });

    [Test]
    public Task The_add_in_picker_leaves_out_the_made_ingredient_being_edited() => UiThread.RunAsync(async () =>
    {
        await MadeCategoryIdAsync();
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        await AddRecipeAsync("Syrup Base", [(sugarId, 1000m)]);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Simple Syrup";
        state.EditingSource = RawMaterialSource.Made;
        PickCategory(state);
        state.EditingWeightLossPercent = 20m;
        await PickRecipeAsync(state, "Syrup Base");
        await state.SaveCommand.ExecuteAsync();

        await Settle.UntilAsync(state, () => state.SelectedRawMaterial?.Name == "Simple Syrup",
            () => $"selected={state.SelectedRawMaterial?.Name ?? "none"}, error={state.ErrorMessage ?? "none"}");

        state.AddInOptions.Should().NotContain(m => m.Name == "Simple Syrup", "a made ingredient can't be an add-in of itself");
        state.AddInOptions.Should().Contain(m => m.Name == "Sugar");
    });

    [Test]
    public Task A_self_referencing_add_in_is_refused_and_nothing_else_is_saved() => UiThread.RunAsync(async () =>
    {
        await MadeCategoryIdAsync();
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        await SeedPurchasedIngredientAsync("Vanilla");
        await AddRecipeAsync("Syrup Base", [(sugarId, 1000m)]);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Simple Syrup";
        state.EditingSource = RawMaterialSource.Made;
        PickCategory(state);
        state.EditingWeightLossPercent = 20m;
        await PickRecipeAsync(state, "Syrup Base");
        state.Recipe.AddIns.Add(new IngredientLineRow { IngredientId = state.Ingredients.Single(m => m.Name == "Vanilla").Id, Grams = 10m });
        await state.SaveCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.SelectedRawMaterial?.Name == "Simple Syrup",
            () => $"selected={state.SelectedRawMaterial?.Name ?? "none"}, error={state.ErrorMessage ?? "none"}");

        // Only reachable by editing the database directly — the picker already leaves the material itself out of
        // AddInOptions — but this pins the guard that catches it anyway, added straight to AddInEdits.
        state.Recipe.AddIns.Add(new IngredientLineRow { IngredientId = state.SelectedRawMaterial!.Id, Grams = 50m });
        state.EditingWeightLossPercent = 25m; // an unrelated edit, which goes with it

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("An ingredient can't be an add-in of itself.");
        state.SuccessMessage.Should().BeNull("nothing was saved");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(m => m.Name == "Simple Syrup", Cancellation)).WeightLossPercent.Should().Be(20m, "the unrelated edit wasn't saved either");
        (await dbContext.IngredientAddInLines.CountAsync(Cancellation)).Should().Be(1, "the bad line was never persisted");
    });

    [Test]
    public Task A_made_ingredient_left_at_the_default_weight_loss_saves_with_3_percent() => UiThread.RunAsync(async () =>
    {
        await MadeCategoryIdAsync();
        await SeedCategoryAsync("Syrups");
        await AddRecipeAsync("Syrup Base", [(await SeedPurchasedIngredientAsync("Sugar"), 1000m)]);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Simple Syrup";
        state.EditingWeightLossPercent.Should().BeNull("a purchased material has no weight loss");
        state.EditingSource = RawMaterialSource.Made;
        PickCategory(state);
        state.EditingWeightLossPercent.Should().Be(3m, "a made ingredient starts at 3%");
        await PickRecipeAsync(state, "Syrup Base");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(m => m.Name == "Simple Syrup", Cancellation)).WeightLossPercent.Should().Be(3m);
    });

    [Test]
    public Task The_header_shows_a_made_ingredients_yield_and_bom_cost_and_its_buffered_cost_follows_them() => UiThread.RunAsync(async () =>
    {
        await MadeCategoryIdAsync();
        var sugarId = await SeedCostedIngredientAsync("Sugar", costPerGram: 0.10m, bufferedCostPerGram: 0.11m);
        var vanillaId = await SeedCostedIngredientAsync("Vanilla", costPerGram: 1m, bufferedCostPerGram: 1.1m);
        await AddRecipeAsync("Syrup Base", [(sugarId, 1000m)]);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Simple Syrup";
        state.EditingSource = RawMaterialSource.Made;
        PickCategory(state);
        state.EditingWeightLossPercent.Should().Be(3m);
        state.Recipe.BomTotalWeight.Should().Be(0m, "no sub-assembly picked yet");
        state.Recipe.BomTotalCost.Should().Be(0m);

        await PickRecipeAsync(state, "Syrup Base");

        state.Recipe.BomTotalWeight.Should().Be(970m, "1,000 g less the 3% lost while making it");
        state.Recipe.BomTotalCost.Should().Be(110m, "1,000 g of sugar at its buffered ₱0.11");
        state.PurchaseCost.BufferedCostText.Should().Be("0.1134", "₱110 over the 970 g it yields, as the recipe stands");

        // An add-in goes on top of the recipe.
        state.Recipe.AddIns.Add(new IngredientLineRow(id => state.Ingredients.FirstOrDefault(m => m.Id == id)) { IngredientId = vanillaId, Grams = 100m });
        state.Recipe.BomTotalWeight.Should().Be(1067m, "1,100 g less 3%");
        state.Recipe.BomTotalCost.Should().Be(220m, "and ₱110 of vanilla");

        // The weight loss moves the yield, not the cost.
        state.EditingWeightLossPercent = 20m;
        state.Recipe.BomTotalWeight.Should().Be(880m);
        state.Recipe.BomTotalCost.Should().Be(220m);
        state.PurchaseCost.BufferedCostText.Should().Be("0.2500");

        await state.SaveCommand.ExecuteAsync();

        await Settle.UntilAsync(state, () => state.SelectedRawMaterial is not null && state.Recipe.AddIns.Count == 1 && state.Recipe.Lines.Count == 1,
            () => $"selected={state.SelectedRawMaterial?.Name ?? "none"}, lines={state.Recipe.AddIns.Count}, error={state.ErrorMessage ?? "none"}");
        state.ErrorMessage.Should().BeNull();
        state.Recipe.BomTotalWeight.Should().Be(880m);
        state.Recipe.BomTotalCost.Should().Be(220m);
        (state.Recipe.BomTotalCost / state.Recipe.BomTotalWeight).Should().Be(state.SelectedRawMaterial!.BufferedCostPerBaseUnit,
            "the tiles are the two sides of the buffered cost the cascade saved");
        state.SelectedRawMaterial!.CostBufferPercent.Should().Be(0m, "a made ingredient carries no buffer of its own");
        state.PurchaseCost.BufferedCostText.Should().Be("0.2500");
    });

    [Test]
    public Task A_purchased_materials_header_keeps_showing_its_saved_buffered_cost() => UiThread.RunAsync(async () =>
    {
        var flourId = await SeedCostedIngredientAsync("Flour", costPerGram: 0.05m, bufferedCostPerGram: 0.0525m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.SelectedRawMaterial = state.RawMaterials.Single(m => m.Id == flourId);

        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => "details never arrived");
        state.PurchaseCost.CurrentCostText.Should().Be("0.0500");
        state.PurchaseCost.BufferedCostText.Should().Be("0.0525", "only a made ingredient's follows the form");
        state.Recipe.BomTotalWeight.Should().Be(0m, "it has no BOM");
        state.Recipe.BomTotalCost.Should().Be(0m);
    });

    [Test]
    public Task A_component_repriced_elsewhere_reaches_the_header_totals_on_the_next_load() => UiThread.RunAsync(async () =>
    {
        var sugarId = await SeedCostedIngredientAsync("Sugar", costPerGram: 0.10m, bufferedCostPerGram: 0.11m);
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup", (sugarId, 1000m));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.SelectedRawMaterial = state.RawMaterials.Single(m => m.Id == syrupId);
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.Lines.Count == 1, () => $"lines={state.Recipe.Lines.Count}");
        state.Recipe.BomTotalCost.Should().Be(110m);

        // Sugar goes up behind the screen's back, as another screen or the cascade would leave it.
        await using (var dbContext = CreateDbContext())
        {
            var sugar = await dbContext.RawMaterials.SingleAsync(m => m.Id == sugarId, Cancellation);
            sugar.BufferedCostPerBaseUnit = 0.22m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await state.LoadCommand.ExecuteAsync();

        state.Recipe.BomTotalCost.Should().Be(220m, "the refreshed component is what the rows point at");
        state.Recipe.BomTotalWeight.Should().Be(1000m, "seeded with no weight loss");
        state.PurchaseCost.BufferedCostText.Should().Be("0.2200");
    });

    [Test]
    public Task The_last_updated_tile_is_the_day_the_cost_last_moved_not_the_day_it_was_saved() => UiThread.RunAsync(async () =>
    {
        var saved = new DateTime(2026, 9, 18, 15, 4, 0, DateTimeKind.Unspecified);
        var costMoved = new DateTime(2026, 9, 21, 9, 30, 0, DateTimeKind.Unspecified);
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup");
        await using (var dbContext = CreateDbContext())
        {
            var syrup = await dbContext.RawMaterials.SingleAsync(m => m.Id == syrupId, Cancellation);
            syrup.UpdatedAt = saved;
            syrup.CostUpdatedAt = costMoved;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.SelectedRawMaterial = state.RawMaterials.Single(m => m.Id == syrupId);

        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => "details never arrived");
        state.LastUpdatedText.Should().Be("Sep 21, 2026", "the tile is one date, the day its cost changed, with no time");
    });

    [Test]
    public Task The_last_updated_tile_falls_back_to_a_dash_for_a_material_whose_cost_has_never_moved() => UiThread.RunAsync(async () =>
    {
        var flourId = await SeedPurchasedIngredientAsync("Flour");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.SelectedRawMaterial = state.RawMaterials.Single(m => m.Id == flourId);

        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => "details never arrived");
        state.LastUpdatedText.Should().Be("—", "it was seeded straight into the database and has never been priced");
    });

    [Test]
    public Task A_cleared_weight_loss_box_saves_as_0_percent() => UiThread.RunAsync(async () =>
    {
        await MadeCategoryIdAsync();
        await SeedCategoryAsync("Syrups");
        await AddRecipeAsync("Syrup Base", [(await SeedPurchasedIngredientAsync("Sugar"), 1000m)]);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Simple Syrup";
        state.EditingSource = RawMaterialSource.Made;
        PickCategory(state);
        await PickRecipeAsync(state, "Syrup Base");
        state.EditingWeightLossPercent = null; // the box shows 0.00 when it's empty

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(m => m.Name == "Simple Syrup", Cancellation)).WeightLossPercent.Should().Be(0m);
    });


    [Test]
    public Task New_after_an_unsaved_new_material_starts_with_no_units_price_or_bom_lines() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Sugar");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        await AddUnitAsync(state, bagId, 25000m);
        AddBrandPrice(state, null, "bag", 90m);
        state.EditingSource = RawMaterialSource.Made;
        state.Recipe.AddIns.Add(new IngredientLineRow { IngredientId = state.Ingredients.Single().Id, Grams = 100m });

        // New over unsaved changes asks about them first: not saving them is how they're left behind.
        state.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        await state.NewCommand.ExecuteAsync();

        state.Units.Rows.Should().BeEmpty();
        state.PurchaseCost.PurchaseUnitOptions.Select(o => o.Name).Should().Equal("gram", "bag");
        state.PurchaseCost.BrandPriceRows.Should().BeEmpty();
        state.PurchaseCost.PurchaseCostText.Should().Be("—");
        state.Recipe.AddIns.Should().BeEmpty();
    });

    [Test]
    public Task The_usage_example_converts_the_quantity_to_grams_and_costs_it() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var flourId = await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var factory = CreateDbContextFactory();
        var bagUnitId = (await new RawMaterialUnitRepository(factory).GetForRawMaterialAsync(flourId, Cancellation)).Single().Id;
        await SetPriceAsync(flourId, bagUnitId, 1250m); // ₱0.05/g

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.UsageExample.Material = state.RawMaterials.Single();
        state.UsageExample.Quantity = 2m;
        state.UsageExample.Unit = state.UsageExample.UnitOptions.Single(o => o.Name == "bag");

        await state.UsageExample.CalculateCommand.ExecuteAsync();

        state.UsageExample.Result.Should().NotBeNull();
        state.UsageExample.Result!.Title.Should().Be("Cost for 2 bag of Flour");
        state.UsageExample.Result.QuantityInBaseUnits.Should().Be(50000m);
        state.UsageExample.Result.BaseUnitAbbreviation.Should().Be("g");
        state.UsageExample.Result.CostPerBaseUnit.Should().Be(0.05m);
        state.UsageExample.Result.TotalCost.Should().Be(2500m);
    });

    [Test]
    public Task A_cost_buffer_is_saved_with_the_material_as_a_fixed_buffered_cost_that_prices_dont_move() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(10m, "a new material starts at Settings' default buffer");
        state.PurchaseCost.EditingBufferedCost.Should().BeNull();
        state.EditingName = "Flour";
        PickCategory(state);
        await AddUnitAsync(state, bagId, 25000m);
        var brand = AddBrandPrice(state, null, "bag", 1250m);
        state.PurchaseCost.TypedCostBufferPercent = 5m;
        state.PurchaseCost.EditingBufferedCost.Should().Be(0.0525m, "typing the percent puts it on top of the dearest brand");
        state.PurchaseCost.PurchaseCostText.Should().Be("0.0500");
        state.PurchaseCost.BufferedCostUnit!.Key.Should().Be(PurchaseUnitChoice.BaseUnitKey, "a new material starts on its base unit");
        state.PurchaseCost.BufferedCostUnit = state.PurchaseCost.BufferedCostUnitOptions.Single(o => o.Name == "bag");
        state.PurchaseCost.PurchaseBufferedCostText.Should().Contain("1,312.50/bag");
        brand.BufferPercent.Should().Be(5m, "the brand's Buffer % column is the buffered cost over its price");
        brand.CostPerBaseUnit.Should().Be(0.05m, "the brand's Unit Price: ₱1,250 a bag of 25,000 g");

        await state.SaveCommand.ExecuteAsync();

        await Settle.UntilAsync(state, () => state.PriceLog.Entries.Count == 1,
            () => $"price rows={state.PriceLog.Entries.Count}, error={state.ErrorMessage ?? "none"}");
        state.ErrorMessage.Should().BeNull();
        state.SelectedRawMaterial!.CostBufferPercent.Should().Be(5m);
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(5m, "the form reloads what was saved");
        state.PurchaseCost.EditingBufferedCost.Should().Be(0.0525m);
        state.PurchaseCost.BufferedCostUnit!.Name.Should().Be("bag", "a saved material starts on its dearest brand's unit");
        state.PurchaseCost.EditingBufferedCostInUnit.Should().Be(1312.5m);
        state.PurchaseCost.HeaderCostUnitText.Should().Be("bag");
        state.PurchaseCost.CurrentCostText.Should().Be("1,250.00");
        state.PurchaseCost.BufferedCostText.Should().Be("1,312.50");

        // The header follows the Per unit: per gram, to 4 decimals.
        state.PurchaseCost.BufferedCostUnit = state.PurchaseCost.BufferedCostUnitOptions[0];
        state.PurchaseCost.HeaderCostUnitText.Should().Be("g");
        state.PurchaseCost.CurrentCostText.Should().Be("0.0500");
        state.PurchaseCost.BufferedCostText.Should().Be("0.0525");
        state.PurchaseCost.BufferedCostUnit = state.PurchaseCost.BufferedCostUnitOptions.Single(o => o.Name == "bag");
        state.PurchaseCost.BufferedCostWarningText.Should().BeEmpty();

        // The bag goes up to ₱1,500 (₱0.06/g): the buffered cost stays, the % follows it over the new price, and the
        // screen says the buffer's gone.
        state.PurchaseCost.BrandPriceRows.Single().Price = 1500m;
        state.PurchaseCost.EditingBufferedCost.Should().Be(0.0525m, "a new price doesn't move the amount");
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(0m, "₱1,312.50 is under ₱1,500, so the buffer's gone");
        state.PurchaseCost.BrandPriceRows.Single().BufferPercent.Should().Be(-12.5m, "₱1,312.50 is 12.5% under ₱1,500");
        state.PurchaseCost.BrandPriceRows.Single().CostPerBaseUnit.Should().Be(0.06m);
        state.PurchaseCost.BufferedCostWarningText.Should().NotBeEmpty();

        // Typing the percent works the buffered cost out from the new dearest price.
        state.PurchaseCost.TypedCostBufferPercent = 10m;
        state.PurchaseCost.EditingBufferedCost.Should().Be(0.066m, "₱0.06 with 10% on top");
        state.PurchaseCost.EditingBufferedCostInUnit.Should().Be(1650m);
        state.PurchaseCost.BufferedCostWarningText.Should().BeEmpty();
        state.PurchaseCost.BrandPriceRows.Single().BufferPercent.Should().Be(10m);

        // Typing the buffered cost works the percent out: ₱1,800 over ₱1,500 is 20%.
        state.PurchaseCost.EditingBufferedCostInUnit = 1800m;
        state.PurchaseCost.EditingBufferedCost.Should().Be(0.072m);
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(20m);
        state.PurchaseCost.TypedCostBufferPercent.Should().Be(20m, "the box shows it");

        // A dearer bag leaves the amount alone and works the % out afresh: ₱1,800 over ₱1,600 is 12.5%.
        state.PurchaseCost.BrandPriceRows.Single().Price = 1600m;
        state.PurchaseCost.EditingBufferedCostInUnit.Should().Be(1800m);
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(12.5m);

        // Typing the percent again puts it back on top of the new dearest price.
        state.PurchaseCost.TypedCostBufferPercent = 20m;
        state.PurchaseCost.EditingBufferedCostInUnit.Should().Be(1920m, "₱1,600 with 20% on top");

        await state.SaveCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.PurchaseCost.BufferedCostText == "1,920.00",
            () => $"buffered={state.PurchaseCost.BufferedCostText}, error={state.ErrorMessage ?? "none"}");

        await state.NewCommand.ExecuteAsync();
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(10m, "the next new material starts at Settings' default again");
        state.PurchaseCost.EditingBufferedCost.Should().BeNull();
    });

    [Test]
    public Task A_kilogram_of_an_ingredient_is_1000_grams_as_its_picked_and_isnt_typed() => UiThread.RunAsync(async () =>
    {
        var kilogramId = await SeedUnitOfMeasureAsync("kilogram", "kg");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();

        // In the Alternative Units grid's add row.
        var kilogram = state.Units.NewRow();
        kilogram.UnitOfMeasureId = kilogramId;
        (kilogram.BaseUnitsPerUnit, kilogram.CanTypeMultiplier).Should().Be((1000m, false));
        var bag = state.Units.NewRow();
        bag.UnitOfMeasureId = bagId;
        (bag.BaseUnitsPerUnit, bag.CanTypeMultiplier).Should().Be((0m, true), "a bag's size is the material's own to type");

        // Picked on a Purchase Cost Setup row, which adds the unit.
        var brand = state.PurchaseCost.NewBrandPriceRow();
        brand.UnitKey = kilogramId;
        brand.Quantity.Should().Be(1000m);
        brand.CanTypeQuantity.Should().BeFalse();
        state.Units.Rows.Should().ContainSingle().Which.BaseUnitsPerUnit.Should().Be(1000m);
    });

    [Test]
    public Task A_length_unit_of_packaging_measured_in_a_length_is_fixed_as_its_picked_and_saved_that_way() => UiThread.RunAsync(async () =>
    {
        var millimeterId = await SeedUnitOfMeasureAsync("millimeter", "mm");
        var meterId = await SeedUnitOfMeasureAsync("meter", "m");
        var inchId = await SeedUnitOfMeasureAsync("inch", "in");
        var rollId = await SeedUnitOfMeasureAsync("roll", "roll");
        var tapesId = await AddPackagingCategoryAsync("Tapes");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Scotch Tape";
        state.EditingType = RawMaterialType.Packaging;
        state.EditingCategory = state.CategoryOptions.Single(c => c.Id == tapesId);
        state.EditingBaseUnit = state.UnitsOfMeasure.Single(u => u.Id == millimeterId);

        // In the Alternative Units grid's add row: metric to metric, and English to metric.
        var meter = state.Units.NewRow();
        meter.UnitOfMeasureId = meterId;
        (meter.BaseUnitsPerUnit, meter.CanTypeMultiplier).Should().Be((1000m, false));
        var inch = state.Units.NewRow();
        inch.UnitOfMeasureId = inchId;
        (inch.BaseUnitsPerUnit, inch.CanTypeMultiplier).Should().Be((25.4m, false));
        var roll = state.Units.NewRow();
        roll.UnitOfMeasureId = rollId;
        (roll.BaseUnitsPerUnit, roll.CanTypeMultiplier).Should().Be((0m, true), "a roll's length is the material's own to type");

        // Picked on a Purchase Cost Setup row, which adds the unit.
        var brand = state.PurchaseCost.NewBrandPriceRow();
        brand.UnitKey = meterId;
        brand.Quantity.Should().Be(1000m);
        brand.CanTypeQuantity.Should().BeFalse();
        state.Units.Rows.Should().ContainSingle().Which.BaseUnitsPerUnit.Should().Be(1000m);

        state.Units.Rows.Add(inch);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var saved = await dbContext.RawMaterialUnits
            .Where(u => u.RawMaterial!.Name == "Scotch Tape")
            .Select(u => new { u.UnitOfMeasure!.Name, u.BaseUnitsPerUnit })
            .ToListAsync(Cancellation);
        saved.Select(u => (u.Name, u.BaseUnitsPerUnit)).Should().BeEquivalentTo([("meter", 1000m), ("inch", 25.4m)]);
    });

    [Test]
    public Task An_unsaved_materials_buffered_cost_per_base_unit_is_shown_to_four_places() => UiThread.RunAsync(async () =>
    {
        // A new material's Per unit starts on the base unit, where the cost is a fraction of a centavo: ₱0.0525/g
        // rounded to the centavo would read as ₱0.05/g, under the percent just typed.
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Flour";
        PickCategory(state);
        await AddUnitAsync(state, bagId, 25000m);
        AddBrandPrice(state, null, "bag", 1250m);
        state.PurchaseCost.TypedCostBufferPercent = 5m;

        state.PurchaseCost.BufferedCostUnit!.Key.Should().Be(PurchaseUnitChoice.BaseUnitKey);
        state.PurchaseCost.EditingBufferedCostInUnit.Should().Be(0.0525m);
        state.PurchaseCost.PurchaseBufferedCostText.Should().Be("0.0525/g is 5% over the dearest brand's 0.0500/g.");
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(5m);

        // On the bag it's to the centavo, as before.
        state.PurchaseCost.BufferedCostUnit = state.PurchaseCost.BufferedCostUnitOptions.Single(o => o.Name == "bag");
        state.PurchaseCost.EditingBufferedCostInUnit.Should().Be(1312.5m);
        state.PurchaseCost.PurchaseBufferedCostText.Should().Be("1,312.50/bag is 5% over the dearest brand's 1,250.00/bag.");
    });

    [Test]
    public Task A_saved_buffered_cost_rounded_to_four_places_doesnt_read_as_under_an_unchanged_dearest_brand() => UiThread.RunAsync(async () =>
    {
        var boxId = await SeedUnitOfMeasureAsync("box", "box");
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        // ₱170 for a box of 240 g is ₱0.708333…/g to 28 places, which the decimal(28,20) column cuts to 20. No buffer, so
        // the buffered cost is the dearest brand's own.
        await state.NewCommand.ExecuteAsync();
        state.PurchaseCost.EditingCostBufferPercent = 0m;
        state.EditingName = "Cello Sheet";
        PickCategory(state);
        await AddUnitAsync(state, boxId, 240m);
        AddBrandPrice(state, null, "box", 170m);
        state.PurchaseCost.BufferedCostWarningText.Should().BeEmpty();

        await state.SaveCommand.ExecuteAsync();

        await Settle.UntilAsync(state, () => state.PriceLog.Entries.Count == 1,
            () => $"price rows={state.PriceLog.Entries.Count}, error={state.ErrorMessage ?? "none"}");
        state.ErrorMessage.Should().BeNull();

        // Opened afresh, as the next session does, the form has what the column kept.
        var reopened = CreateState();
        await reopened.LoadCommand.ExecuteAsync();
        await SelectAsync(reopened, reopened.RawMaterials.Single());
        reopened.PurchaseCost.EditingBufferedCost.Should().Be(0.70833333333333333333m);
        reopened.PurchaseCost.DearestPurchaseCost.Should().BeGreaterThan(reopened.PurchaseCost.EditingBufferedCost!.Value, "the grid works it out to 28 places");
        reopened.PurchaseCost.BufferedCostWarningText.Should().BeEmpty("the dearest brand hasn't moved, only the stored amount was rounded");

        // A real rise still warns.
        reopened.PurchaseCost.BrandPriceRows.Single().Price = 171m;
        reopened.PurchaseCost.BufferedCostWarningText.Should().NotBeEmpty();
    });

    [Test]
    public Task The_buffered_cost_is_typed_in_the_picked_unit_kept_per_base_unit_unrounded_and_only_rounded_for_show() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        await AddUnitAsync(state, bagId, 25000m);
        state.PurchaseCost.BufferedCostUnitOptions.Select(o => o.Name).Should().Equal("gram", "bag");

        state.PurchaseCost.BufferedCostUnit = state.PurchaseCost.BufferedCostUnitOptions.Single(o => o.Name == "bag");
        state.PurchaseCost.EditingBufferedCostInUnit = 1297.37m;
        state.PurchaseCost.EditingBufferedCost.Should().Be(0.0518948m, "₱1,297.37 over 25,000 g, not rounded");
        state.PurchaseCost.EditingBufferedCostInUnit.Should().Be(1297.37m);

        state.PurchaseCost.BufferedCostUnit = state.PurchaseCost.BufferedCostUnitOptions[0];
        state.PurchaseCost.EditingBufferedCostInUnit.Should().Be(0.0519m, "shown to 4 decimals on the base unit, where a centavo is too coarse");
        state.PurchaseCost.EditingBufferedCost.Should().Be(0.0518948m, "which only rounds what's shown");

        // Removing the bag takes the picker back to the base unit.
        state.PurchaseCost.BufferedCostUnit = state.PurchaseCost.BufferedCostUnitOptions.Single(o => o.Name == "bag");
        state.Units.SelectedRow = state.Units.Rows.Single();
        await state.Units.DeleteCommand.ExecuteAsync();
        state.PurchaseCost.BufferedCostUnitOptions.Select(o => o.Name).Should().Equal("gram");
        state.PurchaseCost.BufferedCostUnit!.Key.Should().Be(PurchaseUnitChoice.BaseUnitKey);
    });

    [Test]
    public Task Each_brands_buffer_percent_is_the_buffered_cost_over_its_own_price() => UiThread.RunAsync(async () =>
    {
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        var dearer = state.PurchaseCost.NewBrandPriceRow();
        dearer.BufferPercent.Should().BeNull("there's no price to work a buffer out from");

        dearer.UnitKey = PurchaseUnitChoice.BaseUnitKey;
        dearer.Price = 1m;
        state.PurchaseCost.BrandPriceRows.Add(dearer);
        dearer.BufferPercent.Should().Be(10m, "Settings' default buffer, until one is typed");

        state.PurchaseCost.TypedCostBufferPercent = 5m;
        state.PurchaseCost.EditingBufferedCost.Should().Be(1.05m);
        dearer.BufferPercent.Should().Be(5m);

        // A cheaper brand is further under the same buffered cost: ₱1.05 over ₱0.84 is 25%.
        var cheaper = AddBrandPrice(state, "Cheap", "gram", 0.84m);
        cheaper.BufferPercent.Should().Be(25m);
        (dearer.CostPerBaseUnit, cheaper.CostPerBaseUnit).Should().Be((1m, 0.84m), "each brand's own Unit Price, beside it");

        // Typing the buffered cost works the percent out against the dearest brand, and every row follows.
        state.PurchaseCost.EditingBufferedCostInUnit = 1.2m;
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(20m);
        dearer.BufferPercent.Should().Be(20m);
        cheaper.BufferPercent.Should().BeApproximately(42.857m, 0.001m, "₱1.20 over ₱0.84");

        List<string?> changed = [];
        dearer.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        dearer.Price = 1.5m;
        changed.Should().Contain(nameof(BrandPriceRow.CostPerBaseUnit), "the Unit Price shows the new price");
        dearer.CostPerBaseUnit.Should().Be(1.5m);
        state.PurchaseCost.EditingBufferedCost.Should().Be(1.2m, "a new price doesn't move it");
        dearer.BufferPercent.Should().Be(-20m);
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(0m, "the % follows the price: ₱1.20 is under ₱1.50, so it pins at 0");

        // The % the amount works out to is held to the 0-100 the box and the validator allow, and the buffered
        // cost itself stays exactly as typed.
        state.PurchaseCost.EditingBufferedCostInUnit = 1m;
        state.PurchaseCost.EditingBufferedCost.Should().Be(1m);
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(0m, "₱1.00 is under the dearest ₱1.50, so the buffer's gone");
        state.PurchaseCost.BufferedCostWarningText.Should().NotBeEmpty();

        state.PurchaseCost.EditingBufferedCostInUnit = 4m;
        state.PurchaseCost.EditingBufferedCost.Should().Be(4m);
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(100m, "₱4.00 is 166.67% over ₱1.50, more than the box allows");
    });

    [Test]
    public Task Selecting_a_saved_material_shows_its_price_with_its_buffer_on_top() => UiThread.RunAsync(async () =>
    {
        var flourId = await SeedPurchasedIngredientAsync("Flour");
        await using (var dbContext = CreateDbContext())
        {
            var flour = await dbContext.RawMaterials.SingleAsync(m => m.Id == flourId, Cancellation);
            flour.CostBufferPercent = 5m;
            await dbContext.SaveChangesAsync(Cancellation);
        }
        await SetPriceAsync(flourId, null, 930m);

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.SelectedRawMaterial = state.RawMaterials.Single();

        await Settle.UntilAsync(state, () => state.PurchaseCost.BrandPriceRows.Count == 1,
            () => $"brands={state.PurchaseCost.BrandPriceRows.Count}");
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(5m);
        var brand = state.PurchaseCost.BrandPriceRows.Single();
        (brand.UnitKey, brand.Price, brand.BufferPercent).Should().Be((PurchaseUnitChoice.BaseUnitKey, 930m, 5m));
        state.PurchaseCost.PurchaseCostText.Should().Be("930.0000");
    });

    [Test]
    public Task A_cleared_cost_buffer_box_saves_as_0_percent() => UiThread.RunAsync(async () =>
    {
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Sugar";
        PickCategory(state);
        state.PurchaseCost.EditingCostBufferPercent = null; // the box shows 0.00 when it's empty

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(Cancellation)).CostBufferPercent.Should().Be(0m);
    });

    [Test]
    public Task The_usage_example_shows_the_calculated_cost_the_buffer_and_the_buffered_cost() => UiThread.RunAsync(async () =>
    {
        var flourId = await SeedPurchasedIngredientAsync("Flour");
        await using (var dbContext = CreateDbContext())
        {
            var flour = await dbContext.RawMaterials.SingleAsync(m => m.Id == flourId, Cancellation);
            flour.CostBufferPercent = 5m;
            await dbContext.SaveChangesAsync(Cancellation);
        }
        await SetPriceAsync(flourId, null, 0.05m); // ₱0.05/g, ₱0.0525/g buffered

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.UsageExample.Material = state.RawMaterials.Single();
        state.UsageExample.Quantity = 200m;
        state.UsageExample.Unit = state.UsageExample.UnitOptions.Single(o => o.Name == "gram");

        await state.UsageExample.CalculateCommand.ExecuteAsync();

        state.UsageExample.Result!.TotalCost.Should().Be(10m);
        state.UsageExample.Result.BufferPercent.Should().Be(5m);
        state.UsageExample.Result.BufferedTotalCost.Should().Be(10.5m);
    });

    [Test]
    public Task A_new_material_switched_to_packaging_drops_the_ingredient_only_fields_and_saves_its_own_base_unit() => UiThread.RunAsync(async () =>
    {
        await MadeCategoryIdAsync();
        await SeedPurchasedIngredientAsync("Flour");
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var boxesId = await AddPackagingCategoryAsync("Boxes");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.IsEditingIngredient.Should().BeTrue("a new material starts as an ingredient");
        state.SavedBaseUnitText.Should().Be("gram (g)", "an ingredient's base unit is always the gram");
        state.CategoryOptions.Where(c => c.Id > 0).Should().NotContain(c => c.Id == boxesId, "an ingredient is offered ingredient categories only");
        state.EditingName = "Cake Box";
        state.EditingSource = RawMaterialSource.Made;
        PickCategory(state);
        state.EditingWeightLossPercent = 5m;

        state.EditingType = RawMaterialType.Packaging;
        state.IsEditingPackaging.Should().BeTrue();
        state.EditingSource.Should().Be(RawMaterialSource.Purchased, "packaging is always purchased");
        state.EditingWeightLossPercent.Should().BeNull();
        state.IsEditingMade.Should().BeFalse();
        state.EditingCategory.Should().BeNull("the Made category it had can't hold packaging");

        state.CategoryOptions.Where(c => c.Id > 0).Select(c => c.Id).Should().Equal([boxesId], "packaging is offered packaging categories only");
        state.EditingCategory = state.CategoryOptions.Single(c => c.Id == boxesId);

        state.EditingBaseUnit = state.UnitsOfMeasure.Single(u => u.Id == pieceId);
        state.BaseUnitAbbreviation.Should().Be("pc", "the form names the base unit before the material is saved");
        state.PurchaseCost.PurchaseUnitOptions.Select(o => o.Name).Should().Equal("piece");
        state.SavedBaseUnitText.Should().Be("—", "the Base Unit tile shows the unit only once it's saved");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        var box = state.RawMaterials.Single(m => m.Name == "Cake Box");
        box.Type.Should().Be(RawMaterialType.Packaging);
        box.BaseUnitId.Should().Be(pieceId);
        state.SelectedRawMaterial.Should().BeSameAs(box, "saving a new material selects it");
        state.PurchaseCost.CurrentCostText.Should().Be("0.0000");
        state.SavedBaseUnitText.Should().Be("piece (pc)");
        state.CanChangeTypeAndSource.Should().BeFalse("the type is fixed once saved");
        state.Ingredients.Should().NotContain(box, "the BOM component picker lists ingredients only");
    });

    [Test]
    public Task A_new_material_starts_at_Settings_cost_buffer_and_an_untouched_one_follows_it_when_it_changes() => UiThread.RunAsync(async () =>
    {
        var defaults = new CostingDefaultsRepository(CreateDbContextFactory());
        await defaults.SetAsync(new CostingDefaults { CostBufferPercent = 7.5m }, Cancellation);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();

        state.PurchaseCost.EditingCostBufferPercent.Should().Be(7.5m);
        state.History.IsDirty.Should().BeFalse("the default isn't an edit to save");

        // Changed in Settings while the form is still as New left it: it takes the new default, and still has nothing to save.
        await defaults.SetAsync(new CostingDefaults { CostBufferPercent = 12m }, Cancellation);
        await state.LoadCommand.ExecuteAsync();
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(12m);
        state.History.IsDirty.Should().BeFalse();

        // A buffer typed is kept whatever Settings says next.
        state.PurchaseCost.EditingCostBufferPercent = 4m;
        await defaults.SetAsync(new CostingDefaults { CostBufferPercent = 15m }, Cancellation);
        await state.LoadCommand.ExecuteAsync();
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(4m);

        // New over unsaved changes asks about them first: not saving them is how they're left behind.
        state.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        await state.NewCommand.ExecuteAsync();
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(15m, "the next new material starts at the new default");
    });

    [Test]
    public Task New_starts_a_purchased_ingredient_with_the_other_pickers_empty_and_the_numbers_kept() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await SelectAsync(state, state.RawMaterials.Single());
        AddBrandPrice(state, null, "bag", 1250m);
        state.UsageExample.Material.Should().NotBeNull("selecting a material puts it in the usage example");
        state.PurchaseCost.EditingCostBufferPercent = 5m;
        state.UsageExample.Quantity = 3m;

        // New over unsaved changes asks about them first: not saving them is how they're left behind.
        state.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        await state.NewCommand.ExecuteAsync();

        state.EditingName.Should().BeEmpty();
        state.EditingType.Should().Be(RawMaterialType.Ingredient, "most materials are purchased ingredients");
        state.EditingSource.Should().Be(RawMaterialSource.Purchased);
        state.EditingCategory.Should().BeNull("the category isn't guessed");
        state.PurchaseCost.BrandPriceRows.Should().BeEmpty();
        state.UsageExample.Material.Should().BeNull();
        state.UsageExample.Unit.Should().BeNull();
        state.UsageExample.UnitOptions.Should().BeEmpty();

        state.PurchaseCost.PurchaseCostText.Should().Be("—");
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(10m, "Settings' default buffer, not the last material's");
        state.UsageExample.Quantity.Should().Be(3m, "the usage example's quantity stays");
        state.ShowPurchaseSetup.Should().BeTrue("a new purchased material can be priced before it's saved");
        state.BaseUnitAbbreviation.Should().Be("g");
    });

    [Test]
    public Task Saving_a_new_material_with_its_name_and_category_still_empty_saves_nothing() => UiThread.RunAsync(async () =>
    {
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("Every raw material needs a name. Category is required.");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
    });


    [Test]
    public Task A_price_typed_before_a_purchase_unit_is_picked_is_refused_rather_than_dropped() => UiThread.RunAsync(async () =>
    {
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Flour";
        PickCategory(state);
        var brand = state.PurchaseCost.NewBrandPriceRow();
        brand.Price = 1250m;
        state.PurchaseCost.BrandPriceRows.Add(brand);

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("Pick the purchase unit every price is for, or remove the empty row.");
        state.PurchaseCost.BrandPriceRows.Should().ContainSingle("the edits stay to be fixed");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
    });

    [Test]
    public Task Deleting_a_material_leaves_an_empty_form() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var state = CreateState();
        state.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));
        await state.LoadCommand.ExecuteAsync();

        await SelectAsync(state, state.RawMaterials.Single());
        AddBrandPrice(state, null, "bag", 1250m);

        await state.DeleteCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.RawMaterials.Should().BeEmpty();
        state.EditingName.Should().BeEmpty("the deleted material's details don't stay in the form");
        state.EditingCategory.Should().BeNull();
        state.PurchaseCost.BrandPriceRows.Should().BeEmpty();
        state.UsageExample.Material.Should().BeNull();
        state.CanChangeTypeAndSource.Should().BeTrue("the form is a new material now");
    });

    [Test]
    public Task The_category_picker_lists_just_the_categories_with_no_Add_New_row() => UiThread.RunAsync(async () =>
    {
        await SeedCategoryAsync("Dry goods");
        await SeedCategoryAsync("Butter");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.CategoryOptions.Select(c => c.Name).Should().Equal("Butter", "Dry goods");
    });

    [Test]
    public Task Typing_a_listed_categorys_name_picks_it_whatever_its_case_without_asking() => UiThread.RunAsync(async () =>
    {
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        var prompted = false;
        state.PromptForNewCategory.RegisterHandler(interaction =>
        {
            prompted = true;
            interaction.SetOutput(null);
        });
        await state.LoadCommand.ExecuteAsync();

        state.BeginCategoryEdit();
        TypeCategory(state, "  dry GOODS ");
        await state.CommitCategoryTextAsync();

        prompted.Should().BeFalse("it's a category already");
        state.EditingCategory!.Name.Should().Be("Dry goods");
    });

    // Nothing awaits this: the picker starts it, from the keyboard or the focus leaving.
    [Test]
    public Task A_prompt_for_a_new_category_that_fails_says_so_and_the_next_one_works() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        var failing = true;
        state.PromptForNewCategory.RegisterHandler(interaction =>
        {
            if (failing)
            {
                throw new InvalidOperationException("The prompt couldn't open.");
            }

            interaction.SetOutput("Dry goods");
        });
        await state.LoadCommand.ExecuteAsync();

        state.BeginCategoryEdit();
        TypeCategory(state, "Dry");
        await state.CommitCategoryTextAsync();

        state.ErrorMessage.Should().Contain("The prompt couldn't open.");

        failing = false;
        await state.CommitCategoryTextAsync();

        state.EditingCategory!.Name.Should().Be("Dry goods");
    });

    [Test]
    public Task Typing_a_name_that_isnt_a_category_asks_to_add_it_and_files_the_material_under_it() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        string? promptedWith = null;
        state.PromptForNewCategory.RegisterHandler(interaction =>
        {
            promptedWith = interaction.Input;
            interaction.SetOutput(interaction.Input);
        });
        await state.LoadCommand.ExecuteAsync();

        state.BeginCategoryEdit();
        TypeCategory(state, " Spices ");
        await state.CommitCategoryTextAsync();

        promptedWith.Should().Be("Spices", "the prompt starts with the name typed, so it needn't be typed again");
        state.ErrorMessage.Should().BeNull();
        state.SuccessMessage.Should().Be("Spices was added.", "a toast says so");
        state.EditingCategory!.Name.Should().Be("Spices", "the material is filed under the category just added");
        state.CategorySearchText.Should().Be("Spices");
        state.Categories.Select(c => c.Name).Should().Equal("Spices");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterialCategories.SingleAsync(Cancellation)).Name.Should().Be("Spices");
    });

    [Test]
    public Task A_name_changed_in_the_prompt_to_a_listed_category_picks_that_one_rather_than_adding_it_again() => UiThread.RunAsync(async () =>
    {
        await SeedCategoryAsync("Spices");
        var state = CreateState();
        state.PromptForNewCategory.RegisterHandler(interaction => interaction.SetOutput("spices"));
        await state.LoadCommand.ExecuteAsync();

        state.BeginCategoryEdit();
        TypeCategory(state, "Spice");
        await state.CommitCategoryTextAsync();

        state.EditingCategory!.Name.Should().Be("Spices");
        state.ErrorMessage.Should().BeNull();
        state.Categories.Should().ContainSingle("nothing was added");
    });

    [Test]
    public Task Cancelling_the_new_category_prompt_puts_back_the_category_the_picker_had() => UiThread.RunAsync(async () =>
    {
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        var prompted = false;
        state.PromptForNewCategory.RegisterHandler(interaction =>
        {
            prompted = true;
            interaction.SetOutput(null);
        });
        await state.LoadCommand.ExecuteAsync();
        PickCategory(state);

        state.BeginCategoryEdit();
        TypeCategory(state, "Spi");
        state.EditingCategory.Should().BeNull("the picker lets go of its pick as a name it doesn't list is typed");
        await state.CommitCategoryTextAsync();

        prompted.Should().BeTrue();
        state.EditingCategory!.Name.Should().Be("Dry goods");
        state.CategorySearchText.Should().Be("Dry goods", "the picker shows the category again, not what was typed");
        state.Categories.Should().ContainSingle("nothing was added");
        state.ErrorMessage.Should().BeNull();
    });

    [Test]
    public Task Leaving_the_picker_empty_or_on_its_own_category_asks_nothing() => UiThread.RunAsync(async () =>
    {
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        var prompted = false;
        state.PromptForNewCategory.RegisterHandler(interaction =>
        {
            prompted = true;
            interaction.SetOutput(null);
        });
        await state.LoadCommand.ExecuteAsync();
        PickCategory(state);

        state.BeginCategoryEdit();
        TypeCategory(state, "Dry goods");
        await state.CommitCategoryTextAsync();
        state.EditingCategory!.Name.Should().Be("Dry goods");

        state.BeginCategoryEdit();
        TypeCategory(state, "");
        await state.CommitCategoryTextAsync();

        prompted.Should().BeFalse();
        state.EditingCategory.Should().BeNull("deleting the picker's text leaves the material with no category");
    });

    [Test]
    public Task A_unit_typed_into_the_grid_names_itself_and_is_offered_as_a_purchase_unit_straight_away() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bg");
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();

        // The add row's own item, before the grid commits it: it names its unit as soon as one is picked.
        var row = state.Units.NewRow();
        row.UnitOfMeasureId = bagId;
        row.BaseUnitsPerUnit = 25000m;
        (row.UnitName, row.Abbreviation, row.BaseEquivalentText).Should().Be(("bag", "bg", "25,000 g"));

        state.Units.Rows.Add(row);
        var brand = AddBrandPrice(state, null, "bag", 1000m);
        brand.Quantity.Should().Be(25000m, "a unit added in the grid can be priced in before Save");
        brand.CanTypeQuantity.Should().BeTrue("it isn't saved yet");

        // A multiplier corrected in the grid shows as the brand's quantity, and reprices it.
        row.BaseUnitsPerUnit = 20000m;
        brand.Quantity.Should().Be(20000m);
        state.PurchaseCost.PurchaseCostText.Should().Be("0.0500");
    });

    [Test]
    [Arguments(false, true, "Pick a unit for every alternative unit, or remove the empty row.")]
    [Arguments(true, false, "Enter how many g are in one bag (Alternative Units).")]
    public Task Save_refuses_a_half_filled_unit_row_and_saves_nothing(bool pickUnit, bool typeMultiplier, string message) => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Flour";
        PickCategory(state);
        var row = state.Units.NewRow();
        if (pickUnit)
        {
            row.UnitOfMeasureId = bagId;
        }

        if (typeMultiplier)
        {
            row.BaseUnitsPerUnit = 25000m;
        }

        state.Units.Rows.Add(row);

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(message);
        state.Units.Rows.Should().ContainSingle("the row stays to be fixed");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
    });

    [Test]
    public Task Save_refuses_the_same_unit_typed_twice() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedCategoryAsync("Dry goods");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Flour";
        PickCategory(state);
        await AddUnitAsync(state, bagId, 25000m);
        await AddUnitAsync(state, bagId, 50000m);

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("This material already has that unit.");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
    });

    [Test]
    public Task A_saved_units_multiplier_corrected_in_the_grid_is_saved() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await SelectAsync(state, state.RawMaterials.Single());
        state.Units.Rows.Single().BaseUnitsPerUnit = 20000m;
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterialUnits.SingleAsync(Cancellation)).BaseUnitsPerUnit.Should().Be(20000m);
    });

    [Test]
    public Task Picking_materials_priced_in_units_of_their_own_in_turn_leaves_nothing_to_save() => UiThread.RunAsync(async () =>
    {
        var trayId = await SeedUnitOfMeasureAsync("tray", "tray");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var eggId = await SeedPricedIngredientAsync("Egg Whole", trayId, 1800m, 235m);
        var flourId = await SeedPricedIngredientAsync("All Purpose Flour", bagId, 25000m, 1200m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        // Picked one after the other, as in the list: each shows its buffered cost per its own purchase unit, with nothing
        // to save, however often.
        for (var round = 0; round < 5; round++)
        {
            foreach (var (id, unit) in new[] { (eggId, "tray"), (flourId, "bag") })
            {
                await SelectAsync(state, state.RawMaterials.Single(m => m.Id == id));
                await state.WhenIdleAsync();

                state.PurchaseCost.BufferedCostUnit!.Name.Should().Be(unit, $"round {round + 1}");
                state.History.IsDirty.Should().BeFalse($"round {round + 1}: nothing was typed into {state.EditingName}");
            }
        }
    });

    [Test]
    public Task The_unit_the_buffered_cost_is_shown_in_is_nothing_to_save_but_an_undo_step() => UiThread.RunAsync(async () =>
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var flourId = await SeedPricedIngredientAsync("All Purpose Flour", bagId, 25000m, 1200m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await SelectAsync(state, state.RawMaterials.Single(m => m.Id == flourId));
        IUndoRecorder recorder = state;

        // Picked in the Per box, as its picker does: one undo step.
        recorder.BeginUndoStep();
        state.PurchaseCost.BufferedCostUnit = state.PurchaseCost.BufferedCostUnitOptions.Single(o => o.Key == PurchaseUnitChoice.BaseUnitKey);
        recorder.EndUndoStep();

        state.PurchaseCost.HeaderCostUnitText.Should().Be("g");
        state.History.IsDirty.Should().BeFalse("the buffered cost is kept per base unit, whatever it's shown in");
        state.History.CanUndo.Should().BeTrue();

        state.TryUndo().Should().BeTrue();
        state.PurchaseCost.BufferedCostUnit!.Name.Should().Be("bag");
        state.History.IsDirty.Should().BeFalse();
    });

    /// <summary>A purchased ingredient with one alternative unit and one brand priced in it, saved as the screen saves.</summary>
    private async Task<int> SeedPricedIngredientAsync(string name, int unitOfMeasureId, decimal baseUnitsPerUnit, decimal price)
    {
        var id = await SeedPurchasedIngredientAsync(name, (unitOfMeasureId, baseUnitsPerUnit));
        var repository = new RawMaterialRepository(CreateDbContextFactory());
        var material = (await repository.GetByIdAsync(id, Cancellation))!;
        await repository.SaveAsync(material, [.. material.Units], [("House", unitOfMeasureId, price)], null, Cancellation);
        return id;
    }

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

    /// <summary>
    /// Types an alternative unit into the grid's add row as the grid does: it edits the row the screen hands it
    /// (NewUnitRow), then adds it to the rows when it's committed. Only Save writes it.
    /// </summary>
    private static Task AddUnitAsync(RawMaterialsState state, int unitOfMeasureId, decimal baseUnitsPerUnit)
    {
        var row = state.Units.NewRow();
        row.UnitOfMeasureId = unitOfMeasureId;
        row.BaseUnitsPerUnit = baseUnitsPerUnit;
        state.Units.Rows.Add(row);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Types a brand into the Purchase Cost Setup grid's add row as the grid does: it edits the row the screen hands it
    /// (NewBrandPriceRow), picking the unit named <paramref name="unitName"/>, then adds it to the rows when it's
    /// committed. Only Save writes it.
    /// </summary>
    [Test]
    public Task A_purchase_unit_later_made_the_base_unit_is_priced_in_the_base_unit_and_saves() => UiThread.RunAsync(async () =>
    {
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        await AddPackagingCategoryAsync("Boxes");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Cake Box";
        state.EditingType = RawMaterialType.Packaging;
        PickCategory(state);

        // Priced by the piece before the base unit is picked: the piece is added as an alternative unit.
        var brand = AddBrandPrice(state, null, "piece", 5m);
        brand.Quantity = 1m;
        state.Units.Rows.Select(r => r.UnitName).Should().Equal("piece");

        state.EditingBaseUnit = state.UnitsOfMeasure.Single(u => u.Id == pieceId);

        (brand.UnitKey, brand.Quantity, brand.CanTypeQuantity).Should().Be((PurchaseUnitChoice.BaseUnitKey, 1m, false));
        state.Units.Rows.Should().BeEmpty("the piece is the base unit now, which can't be an alternative unit too");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterialUnits.CountAsync(Cancellation)).Should().Be(0);
        (await dbContext.RawMaterialBrandPrices.SingleAsync(Cancellation)).Should().BeEquivalentTo(new { UnitId = (int?)null, Price = 5m });
    });

    private static BrandPriceRow AddBrandPrice(RawMaterialsState state, string? brand, string unitName, decimal price)
    {
        var row = state.PurchaseCost.NewBrandPriceRow();
        row.Brand = brand;
        row.UnitKey = state.PurchaseCost.PurchaseUnitOptions.Single(o => o.Name == unitName).Key;
        row.Price = price;
        state.PurchaseCost.BrandPriceRows.Add(row);
        return row;
    }

    /// <summary>
    /// Types into the Category picker: the text lands, and a name the picker doesn't list (exactly, whatever its case)
    /// clears the category it held, as typing can (CategoryPicker.Begin).
    /// </summary>
    private static void TypeCategory(RawMaterialsState state, string text)
    {
        state.CategorySearchText = text;
        if (!state.CategoryOptions.Any(c => string.Equals(c.Name, text, StringComparison.OrdinalIgnoreCase)))
        {
            state.EditingCategory = null;
        }
    }

    /// <summary>Picks the first category, which a new material starts without.</summary>
    private static void PickCategory(RawMaterialsState state) =>
        state.EditingCategory = state.Categories.First(c => c.Type == RawMaterialRules.CategoryTypeFor(state.EditingType, state.EditingSource));

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

    /// <summary>A purchased ingredient with its costs written straight onto the row: a true cost per gram and a buffered one.</summary>
    private async Task<int> SeedCostedIngredientAsync(string name, decimal costPerGram, decimal bufferedCostPerGram)
    {
        var id = await SeedPurchasedIngredientAsync(name);
        await using var dbContext = CreateDbContext();
        var ingredient = await dbContext.RawMaterials.SingleAsync(m => m.Id == id, Cancellation);
        ingredient.CostPerBaseUnit = costPerGram;
        ingredient.BufferedCostPerBaseUnit = bufferedCostPerGram;
        await dbContext.SaveChangesAsync(Cancellation);
        return id;
    }

    /// <summary>
    /// A made ingredient made from a recipe of <paramref name="lines"/> (grams of each), named "{name} Base", or from an
    /// empty one for none. Inserted past the repository, so its cost isn't worked out: the screen works its tiles out itself.
    /// </summary>
    private async Task<int> SeedMadeIngredientAsync(string name, params (int ComponentId, decimal Grams)[] lines)
    {
        var recipeId = lines.Length == 0 ? await AddEmptyRecipeAsync() : await AddRecipeAsync($"{name} Base", [.. lines]);
        await using var dbContext = CreateDbContext();
        var ingredient = new RawMaterial
        {
            Name = name,
            Type = RawMaterialType.Ingredient,
            Category = new RawMaterialCategory { Name = $"{name} goods" },
            Source = RawMaterialSource.Made,
            WeightLossPercent = 0m,
            SubAssemblyId = recipeId,
        };
        dbContext.RawMaterials.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return ingredient.Id;
    }

    /// <summary>Picks the sub-assembly named <paramref name="name"/> on the Recipe card, and waits for its lines to show.</summary>
    private static async Task PickRecipeAsync(RawMaterialsState state, string name)
    {
        state.Recipe.SubAssembly = state.Recipe.SubAssemblyOptions.Single(s => s.Name == name);
        await Settle.UntilAsync(state, () => state.Recipe.Lines.Count > 0, () => $"recipe lines={state.Recipe.Lines.Count}");
    }

    /// <summary>Renames a unit of measure's abbreviation the way Settings would, behind the screen's back.</summary>
    private async Task RenameAbbreviationAsync(int unitOfMeasureId, string abbreviation)
    {
        await using var dbContext = CreateDbContext();
        var unit = await dbContext.UnitsOfMeasure.FindAsync([unitOfMeasureId], Cancellation);
        unit!.Abbreviation = abbreviation;
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private async Task<int> AddInIdAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.IngredientAddInLines.Select(l => l.Id).SingleAsync(Cancellation);
    }

    /// <summary>
    /// Picks <paramref name="material"/> as a click in the list does, and waits for its details (price history, brand
    /// prices, BOM), which are read off the UI thread: until they arrive the previous material's show, and Save waits.
    /// </summary>
    private static async Task SelectAsync(RawMaterialsState state, RawMaterial material)
    {
        state.SelectedRawMaterial = material;
        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => $"{material.Name}'s details never arrived");
    }

    [Test]
    public Task The_forms_version_goes_up_as_another_material_or_a_new_one_takes_the_form() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.OpenRecord(flourId);
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.EditingName == "Flour", () => $"name={state.EditingName}");
        var flour = state.FormVersion;

        // What the boxes hold is still the material's: they keep the cursor.
        state.EditingName = "Flour, first class";
        await state.LoadCommand.ExecuteAsync();
        state.FormVersion.Should().Be(flour, "typing and a reload leave the form the same material's");

        state.OpenRecord(sugarId);
        var sugar = state.FormVersion;
        sugar.Should().BeGreaterThan(flour);

        await state.NewCommand.ExecuteAsync();
        var fresh = state.FormVersion;
        fresh.Should().BeGreaterThan(sugar);

        await state.NewCommand.ExecuteAsync();
        var freshAgain = state.FormVersion;
        freshAgain.Should().BeGreaterThan(fresh, "a new one in a new one's place empties the boxes too");

        // New, then the browser's Back: the Name box had the cursor, and stayed blank over the material's name.
        state.OpenRecord(flourId);
        state.FormVersion.Should().BeGreaterThan(freshAgain);
    });

    [Test]
    public Task The_material_in_the_screens_address_is_opened_once_the_load_is_back() => UiThread.RunAsync(async () =>
    {
        var sugarId = await AddIngredientAsync("Sugar");
        var state = CreateState();

        // As the page does when it opens on /raw-materials/42, before the load it then runs.
        state.OpenRecordOnLoad(sugarId);
        state.SelectedRawMaterial.Should().BeNull("the list is empty until the screen loads");

        await state.LoadCommand.ExecuteAsync();

        state.SelectedRawMaterial!.Id.Should().Be(sugarId);
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.EditingName == "Sugar", () => $"name={state.EditingName}");
    });

    [Test]
    public Task New_clicked_while_the_screen_is_still_opening_the_material_in_its_address_keeps_the_new_form() => UiThread.RunAsync(async () =>
    {
        var sugarId = await AddIngredientAsync("Sugar");
        var state = CreateState();
        state.OpenRecordOnLoad(sugarId);
        var loading = state.LoadCommand.ExecuteAsync();

        // The load is still reading. Sugar opened over the new form at its end, and the name typed was saved as Sugar's.
        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Muscovado";
        await loading;
        await state.LoadCommand.ExecuteAsync();

        state.SelectedRawMaterial.Should().BeNull("New was the later word");
        state.EditingName.Should().Be("Muscovado");
        state.RawMaterials.Should().ContainSingle().Which.Name.Should().Be("Sugar");
    });
}
