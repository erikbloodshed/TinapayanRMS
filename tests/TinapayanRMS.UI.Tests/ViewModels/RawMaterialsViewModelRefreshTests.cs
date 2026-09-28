using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

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

        await SelectAsync(viewModel, viewModel.RawMaterials.Single());
        viewModel.EditingName = "Flour (unsaved rename)";
        viewModel.CanAddUnit.Should().BeTrue("one alternative unit is below the limit of two");
        var selected = viewModel.SelectedRawMaterial;
        var pickedUnit = viewModel.UnitRows.Single().Unit.UnitOfMeasure;

        await SeedUnitOfMeasureAsync("sack", "sk");
        await viewModel.LoadCommand.Execute();

        // The grid and the picker track their selection by object, so the lists must still hold
        // the very objects that were selected, not fresh copies of them.
        viewModel.RawMaterials.Should().ContainSingle().Which.Should().BeSameAs(selected);
        viewModel.SelectedRawMaterial.Should().BeSameAs(selected);
        viewModel.UnitsOfMeasure.Should().Contain(unit => ReferenceEquals(unit, pickedUnit));
        viewModel.UnitRows.Single().Unit.UnitOfMeasure.Should().BeSameAs(pickedUnit, "the grid's unit cell still finds the unit it shows");
        viewModel.EditingName.Should().Be("Flour (unsaved rename)");
        viewModel.UnitsOfMeasure.Select(u => u.Name).Should().Equal("bag", "sack");
    }

    [Fact]
    public async Task OpenRawMaterial_waits_for_a_list_not_loaded_yet_and_clears_a_search_hiding_it()
    {
        var flourId = await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");
        var viewModel = CreateViewModel();

        // As the main window's Back does before the Raw Materials screen was loaded.
        viewModel.OpenRawMaterial(sugarId);
        viewModel.SelectedRawMaterial.Should().BeNull("the list is empty until the screen loads");

        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial!.Id.Should().Be(sugarId);
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady && viewModel.EditingName == "Sugar", () => $"name={viewModel.EditingName}");

        viewModel.SearchText = "Flour";
        viewModel.OpenRawMaterial(flourId);
        viewModel.SelectedRawMaterial!.Id.Should().Be(flourId);
        viewModel.SearchText.Should().Be("Flour", "a search showing it is left alone");

        viewModel.OpenRawMaterial(sugarId);
        viewModel.SelectedRawMaterial!.Id.Should().Be(sugarId);
        viewModel.SearchText.Should().BeEmpty("a search hiding it is cleared, so the list shows the row picked");
        viewModel.VisibleRawMaterials.Should().Contain(viewModel.SelectedRawMaterial);
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady, () => "Sugar's details never arrived");
    }

    [Fact]
    public async Task Reloading_shows_a_unit_renamed_on_another_tab_without_reloading_the_form()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var flourId = await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await SelectAsync(viewModel, viewModel.RawMaterials.Single());
        viewModel.EditingName = "Flour (unsaved rename)";

        await RenameAbbreviationAsync(bagId, "bg");
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial!.Id.Should().Be(flourId);
        viewModel.SelectedRawMaterial.Units.Should().ContainSingle().Which.UnitOfMeasure!.Abbreviation.Should().Be("bg", "the row shows the renamed unit");
        var row = viewModel.UnitRows.Should().ContainSingle().Subject;
        (row.Abbreviation, row.BaseEquivalentText).Should().Be(("bg", "25,000 g"));
        viewModel.EditingName.Should().Be("Flour (unsaved rename)", "re-pointing the selection must not reload the form");
    }


    [Fact]
    public async Task A_second_pick_before_the_first_ones_details_arrive_shows_only_its_own()
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
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        // Sugar picked before Flour's details can arrive, as a quick second click or an arrow key held down in the list does.
        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(m => m.Id == flourId);
        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(m => m.Id == sugarId);

        await WaitUntilSettledAsync(() => viewModel.BrandPriceRows.Count > 0 && viewModel.PriceHistory.Count > 0,
            () => $"brands={viewModel.BrandPriceRows.Count}, prices={viewModel.PriceHistory.Count}");
        viewModel.BrandPriceRows.Select(r => (r.Brand, r.Price)).Should().Equal([(null, 0.09m)], "Flour's brands never join Sugar's");
        viewModel.PriceHistory.Should().ContainSingle().Which.Entry.RawMaterialId.Should().Be(sugarId);

        // The same for a made ingredient's bill of materials.
        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(m => m.Name == "Butter Icing");
        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(m => m.Name == "Syrup");

        await WaitUntilSettledAsync(() => viewModel.BomLineEdits.Count > 0, () => $"lines={viewModel.BomLineEdits.Count}");
        viewModel.BomLineEdits.Select(l => (l.Ingredient!.Name, l.Grams)).Should().Equal([("Sugar", 100m)], "Butter Icing's lines never join Syrup's");
    }

    [Fact]
    public async Task Picking_another_material_keeps_the_previous_details_until_its_own_arrive_and_Save_waits_for_them()
    {
        var flourId = await SeedPurchasedIngredientAsync("Flour");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar");
        await CreateBrandPriceRepository().SetAsync(
            flourId,
            [new RawMaterialBrandPrice { Brand = "Gold Medal", Price = 0.05m }, new RawMaterialBrandPrice { Brand = "Wellington", Price = 0.04m }],
            Cancellation);
        await SetPriceAsync(sugarId, null, 0.09m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await SelectAsync(viewModel, viewModel.RawMaterials.Single(m => m.Id == flourId));

        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(m => m.Id == sugarId);

        // Its details are still on their way (read off the UI thread): Flour's show until they arrive, not an empty grid,
        // and a Save now would write Flour's brand prices under Sugar.
        viewModel.AreDetailsReady.Should().BeFalse();
        viewModel.EditingName.Should().Be("Sugar");
        viewModel.BrandPriceRows.Select(r => r.Brand).Should().Equal("Gold Medal", "Wellington");
        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady && viewModel.BrandPriceRows.Count == 1,
            () => $"ready={viewModel.AreDetailsReady}, brands={viewModel.BrandPriceRows.Count}");
        viewModel.BrandPriceRows.Single().Price.Should().Be(0.09m);
        viewModel.SuccessMessage.Should().BeNull("nothing was saved");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterialBrandPrices.Where(p => p.RawMaterialId == sugarId).Select(p => p.Price).ToListAsync(Cancellation))
            .Should().Equal([0.09m], "the Save did nothing");
    }

    [Fact]
    public async Task Unsaved_units_survive_a_reload_and_show_a_unit_renamed_on_another_tab()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Flour");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await SelectAsync(viewModel, viewModel.RawMaterials.Single());
        await AddUnitAsync(viewModel, bagId, 25000m);
        var brand = AddBrandPrice(viewModel, "Gold Medal", "bag", 1250m);

        await RenameAbbreviationAsync(bagId, "bg");
        await viewModel.LoadCommand.Execute();

        var row = viewModel.UnitRows.Should().ContainSingle().Subject;
        row.Unit.Id.Should().Be(0, "the unit still isn't saved");
        (row.Abbreviation, row.BaseEquivalentText).Should().Be(("bg", "25,000 g"));
        viewModel.BrandPriceRows.Should().ContainSingle().Which.Should().BeSameAs(brand, "the unsaved brand stays");
        viewModel.PurchaseUnitOptions.Single(o => o.Key == brand.UnitKey).Abbreviation.Should().Be("bg", "the unit picked stays picked");
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
        AddBrandPrice(viewModel, "Gold Medal", "bag", 1250m);

        await AddUnitAsync(viewModel, packId, 1000m);
        viewModel.CanAddUnit.Should().BeFalse("two alternative units is the limit, so the grid's add row goes");
        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.PriceHistory.Count == 1 && viewModel.BrandPriceRows.Count == 1,
            () => $"price rows={viewModel.PriceHistory.Count}, brands={viewModel.BrandPriceRows.Count}, error={viewModel.ErrorMessage ?? "none"}");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Flour was saved.", "a toast says so");
        viewModel.SelectedRawMaterial!.Name.Should().Be("Flour", "saving a new material selects it");
        viewModel.UnitRows.Select(r => (r.UnitName, r.Unit.Id > 0)).Should().Equal(("bag", true), ("pack", true));
        viewModel.PriceHistory.Single().Entry.UnitId.Should().Be(viewModel.UnitRows[0].Unit.Id);
        viewModel.PriceHistory.Single().Brand.Should().Be("Gold Medal");
        var brand = viewModel.BrandPriceRows.Single();
        (brand.Brand, brand.UnitKey, brand.Price).Should().Be(("Gold Medal", bagId, 1250m), "the form reloads what was saved");
        viewModel.HeaderCostUnitText.Should().Be("bag", "the header's costs are in the Per unit, the dearest brand's");
        viewModel.CurrentCostText.Should().Be("1,250.00");
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
        var sackId = await SeedUnitOfMeasureAsync("sack", "sk");
        await SeedUnitOfMeasureAsync("gram", "g");
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Flour";
        PickCategory(viewModel);
        viewModel.PurchaseUnitOptions.Select(o => o.Name).Should().Equal(
            ["gram", "bag", "sack"], "a unit of measure named like the ingredient's gram isn't offered twice");

        // The grid's add row edits the screen's own row, which adds the unit as soon as it's picked.
        var brand = viewModel.NewBrandPriceRow();
        brand.CanTypeQuantity.Should().BeFalse("there's no unit to type a quantity for");
        brand.UnitKey = sackId;

        var row = viewModel.UnitRows.Should().ContainSingle().Subject;
        row.UnitName.Should().Be("sack");
        row.Unit.Id.Should().Be(0, "it isn't saved yet");
        brand.CanTypeQuantity.Should().BeTrue();
        brand.Quantity.Should().BeNull("the unit has no quantity until it's typed");

        brand.Quantity = 25000m;
        brand.Price = 1500m;
        viewModel.BrandPriceRows.Add(brand);
        viewModel.UnitRows.Single().BaseEquivalentText.Should().Be("25,000 g", "the quantity typed is the unit's multiplier");
        viewModel.PurchaseCostText.Should().Be("0.0600");

        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.PriceHistory.Count == 1 && viewModel.BrandPriceRows.Count == 1,
            () => $"price rows={viewModel.PriceHistory.Count}, brands={viewModel.BrandPriceRows.Count}, error={viewModel.ErrorMessage ?? "none"}");
        viewModel.ErrorMessage.Should().BeNull();
        var saved = viewModel.UnitRows.Should().ContainSingle().Subject;
        saved.Unit.Id.Should().BeGreaterThan(0);
        saved.BaseUnitsPerUnit.Should().Be(25000m);
        viewModel.PriceHistory.Single().Entry.UnitId.Should().Be(saved.Unit.Id);
        var reloaded = viewModel.BrandPriceRows.Single();
        (reloaded.UnitKey, reloaded.Quantity).Should().Be((sackId, 25000m));
        reloaded.CanTypeQuantity.Should().BeFalse("a saved unit's multiplier is corrected in Alternative Units");
        viewModel.CurrentCostText.Should().Be("1,500.00", "a 25 kg sack's worth, the Per unit");
    }


    [Fact]
    public async Task Picking_another_purchase_unit_takes_back_the_one_added_there()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var sackId = await SeedUnitOfMeasureAsync("sack", "sk");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();

        var brand = viewModel.NewBrandPriceRow();
        brand.UnitKey = sackId;
        viewModel.UnitRows.Select(r => r.UnitName).Should().Equal("sack");

        brand.UnitKey = bagId;
        viewModel.UnitRows.Select(r => r.UnitName).Should().Equal(["bag"], "picking another new unit replaces the one added");

        brand.UnitKey = PurchaseUnitChoice.BaseUnitKey;
        viewModel.UnitRows.Should().BeEmpty("the unit was only there for the brand");
        brand.Quantity.Should().Be(1m);
    }


    [Fact]
    public async Task A_saved_units_quantity_is_read_only_and_new_units_are_added_while_there_is_room_for_one()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var sackId = await SeedUnitOfMeasureAsync("sack", "sk");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await SelectAsync(viewModel, viewModel.RawMaterials.Single());
        var bag = AddBrandPrice(viewModel, "Gold Medal", "bag", 1250m);
        bag.CanTypeQuantity.Should().BeFalse("the bag unit is saved");
        bag.Quantity.Should().Be(25000m);

        var pack = AddBrandPrice(viewModel, "Wellington", "pack", 40m);
        viewModel.UnitRows.Select(r => r.UnitName).Should().Equal("bag", "pack");
        viewModel.CanAddUnit.Should().BeFalse("that's the two alternative units allowed");

        var sack = viewModel.NewBrandPriceRow();
        sack.UnitKey = sackId;
        viewModel.ErrorMessage.Should().StartWith("A material can have at most 2 alternative units.");
        viewModel.UnitRows.Select(r => r.UnitName).Should().Equal(["bag", "pack"], "there's no room for a third");

        pack.UnitKey = sackId;
        viewModel.UnitRows.Select(r => r.UnitName).Should().Equal(["bag", "sack"], "the unit added for a brand can still be swapped for another");
    }


    [Fact]
    public async Task Saving_a_unit_added_for_a_brand_without_its_quantity_saves_nothing()
    {
        await SeedUnitOfMeasureAsync("sack", "sk");
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Flour";
        PickCategory(viewModel);
        AddBrandPrice(viewModel, null, "sack", 1500m);

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("Enter how many g are in one sack (Qty per Purchase Unit).");
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

        await SelectAsync(viewModel, viewModel.RawMaterials.Single());
        viewModel.ShowPriceHistory.Should().BeTrue();
        viewModel.SelectedDetailsTab = RawMaterialsViewModel.PriceHistoryTab;

        await viewModel.NewCommand.Execute();

        viewModel.ShowPriceHistory.Should().BeFalse("a new material has no price history");
        viewModel.SelectedDetailsTab.Should().Be(0, "a hidden tab can't stay the one showing");
    }

    [Fact]
    public async Task Used_By_lists_the_picked_materials_products_follows_other_tabs_and_opens_one_on_a_tile()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [(flourId, 1000m)]);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await SelectAsync(viewModel, viewModel.RawMaterials.Single(m => m.Id == flourId));
        viewModel.UsedBy.Select(p => p.Name).Should().Equal("Pandesal");
        viewModel.UsedByText.Should().StartWith("These products use it");

        // A product made on the Products tab while this screen kept Flour open.
        await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m), (sugarId, 100m)]);
        await viewModel.LoadCommand.Execute();
        viewModel.UsedBy.Select(p => p.Name).Should().Equal("Ensaymada", "Pandesal");

        var opened = new List<int>();
        using (viewModel.ProductOpenRequests.Subscribe(opened.Add))
        {
            await viewModel.OpenProductCommand.Execute(pandesalId);
        }

        opened.Should().Equal(pandesalId);

        await viewModel.NewCommand.Execute();
        viewModel.UsedBy.Should().BeEmpty("a new material isn't used by anything");
        viewModel.UsedByText.Should().Be("No product uses it yet.");
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
        AddBrandPrice(viewModel, "Gold Medal", "bag", 1250m);

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("That name is already in use.");
        viewModel.SuccessMessage.Should().BeNull("nothing was saved");
        viewModel.SelectedRawMaterial.Should().BeNull();
        viewModel.CanChangeTypeAndSource.Should().BeTrue("the material still isn't saved");
        viewModel.UnitRows.Should().ContainSingle().Which.UnitName.Should().Be("bag");
        viewModel.BrandPriceRows.Should().ContainSingle().Which.Price.Should().Be(1250m);

        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.CountAsync(Cancellation)).Should().Be(1);
        (await dbContext.RawMaterialUnits.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.RawMaterialBrandPrices.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.RawMaterialPriceHistories.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Leaving_the_name_field_flags_a_name_already_taken()
    {
        await SeedPurchasedIngredientAsync("Flour");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "  flour  "; // different case, padded — still the same name

        viewModel.CheckNameAvailability();

        viewModel.ErrorMessage.Should().Be("That name is already in use.");
    }

    [Fact]
    public async Task Leaving_the_name_field_does_not_flag_the_materials_own_name_and_clears_once_fixed()
    {
        await SeedPurchasedIngredientAsync("Flour");
        await SeedPurchasedIngredientAsync("Sugar");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        // Selecting Flour and leaving its name untouched isn't a conflict with itself.
        await SelectAsync(viewModel, viewModel.RawMaterials.Single(m => m.Name == "Flour"));
        viewModel.CheckNameAvailability();
        viewModel.ErrorMessage.Should().BeNull();

        // Renaming it to another material's name is flagged...
        viewModel.EditingName = "Sugar";
        viewModel.CheckNameAvailability();
        viewModel.ErrorMessage.Should().Be("That name is already in use.");

        // ...and fixing it clears the message this check raised.
        viewModel.EditingName = "Cake Flour";
        viewModel.CheckNameAvailability();
        viewModel.ErrorMessage.Should().BeNull("the name is free again, so the message it raised clears itself");
    }


    [Fact]
    public async Task Saving_a_price_in_an_alternative_unit_records_the_unit_and_prices_the_ingredient_per_gram()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m), (packId, 1000m));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await SelectAsync(viewModel, viewModel.RawMaterials.Single());
        viewModel.CanAddUnit.Should().BeFalse("the ingredient already has the two alternative units allowed");

        AddBrandPrice(viewModel, null, "pack", 60m);
        viewModel.PurchaseCostText.Should().Be("0.0600", "the cost per gram is worked out as the price is typed");
        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.PriceHistory.Count == 1 && viewModel.BrandPriceRows.Count == 1,
            () => $"price rows={viewModel.PriceHistory.Count}, brands={viewModel.BrandPriceRows.Count}, error={viewModel.ErrorMessage ?? "none"}");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.PriceHistory.Single().UnitName.Should().Be("pack");
        viewModel.PriceHistory.Single().CostPerBaseUnit.Should().Be(0.06m);
        viewModel.SelectedRawMaterial!.CostPerBaseUnit.Should().Be(0.06m);
        (viewModel.BrandPriceRows.Single().UnitKey, viewModel.BrandPriceRows.Single().Price).Should().Be((packId, 60m));
    }

    [Fact]
    public async Task The_material_is_costed_at_the_dearest_brand_and_the_summary_says_which()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m), (packId, 1000m));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await SelectAsync(viewModel, viewModel.RawMaterials.Single());
        AddBrandPrice(viewModel, "Gold Medal", "bag", 900m); // ₱0.036/g
        AddBrandPrice(viewModel, "Wellington", "pack", 40m); // ₱0.04/g
        viewModel.PurchaseCostText.Should().Be("0.0400");
        viewModel.PurchaseCostSourceText.Should().Be("From Wellington, the dearest of 2 brands.");

        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.PriceHistory.Count == 1 && viewModel.BrandPriceRows.Count == 2,
            () => $"price rows={viewModel.PriceHistory.Count}, brands={viewModel.BrandPriceRows.Count}, error={viewModel.ErrorMessage ?? "none"}");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SelectedRawMaterial!.CostPerBaseUnit.Should().Be(0.04m);
        (viewModel.PriceHistory.Single().Brand, viewModel.PriceHistory.Single().UnitName).Should().Be(("Wellington", "pack"));
    }

    [Fact]
    public async Task Removing_a_brand_takes_back_the_unit_added_for_it()
    {
        await SeedUnitOfMeasureAsync("sack", "sk");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();

        var brand = AddBrandPrice(viewModel, "Gold Medal", "sack", 1500m);
        viewModel.UnitRows.Select(r => r.UnitName).Should().Equal("sack");

        viewModel.SelectedBrandPriceRow = brand;
        await viewModel.RemoveBrandPriceCommand.Execute();

        viewModel.BrandPriceRows.Should().BeEmpty();
        viewModel.UnitRows.Should().BeEmpty("the unit was only there for the brand");
        viewModel.PurchaseCostText.Should().Be("—");
    }

    [Fact]
    public async Task Save_refuses_a_brand_priced_twice_in_the_same_unit()
    {
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Sugar";
        PickCategory(viewModel);
        AddBrandPrice(viewModel, "Bounty", "gram", 0.09m);
        AddBrandPrice(viewModel, "bounty ", "gram", 0.1m);

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("'Bounty' is priced twice in the same unit. Remove one.");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
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
        viewModel.HeaderCostUnitText.Should().Be("g", "a made ingredient has no Purchase Cost Setup, so its header stays per gram");
        viewModel.CurrentCostText.Should().MatchRegex(@"^\d+\.\d{4}$");
        var lineId = await BomLineIdAsync();

        viewModel.EditingName = "Syrup";
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        (await BomLineIdAsync()).Should().Be(lineId, "an untouched BOM isn't sent, so its line isn't replaced");
    }

    [Fact]
    public async Task The_BOM_component_picker_leaves_out_the_made_ingredient_being_edited()
    {
        await SeedPurchasedIngredientAsync("Sugar");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Simple Syrup";
        PickCategory(viewModel);
        viewModel.EditingSource = RawMaterialSource.Made;
        viewModel.EditingWeightLossPercent = 20m;
        viewModel.BomLineEdits.Add(new IngredientLineRow { IngredientId = viewModel.Ingredients.Single().Id, Grams = 1000m });
        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.SelectedRawMaterial?.Name == "Simple Syrup",
            () => $"selected={viewModel.SelectedRawMaterial?.Name ?? "none"}, error={viewModel.ErrorMessage ?? "none"}");

        viewModel.BomComponentOptions.Should().NotContain(m => m.Name == "Simple Syrup", "a made ingredient can't be a component of itself");
        viewModel.BomComponentOptions.Should().Contain(m => m.Name == "Sugar");
    }

    [Fact]
    public async Task A_self_referencing_BOM_line_is_refused_even_when_the_rest_of_the_BOM_is_left_untouched()
    {
        await SeedPurchasedIngredientAsync("Sugar");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Simple Syrup";
        PickCategory(viewModel);
        viewModel.EditingSource = RawMaterialSource.Made;
        viewModel.EditingWeightLossPercent = 20m;
        viewModel.BomLineEdits.Add(new IngredientLineRow { IngredientId = viewModel.Ingredients.Single().Id, Grams = 1000m });
        await viewModel.SaveCommand.Execute();
        await WaitUntilSettledAsync(() => viewModel.SelectedRawMaterial?.Name == "Simple Syrup",
            () => $"selected={viewModel.SelectedRawMaterial?.Name ?? "none"}, error={viewModel.ErrorMessage ?? "none"}");

        // Only reachable by editing the database directly — the picker already leaves the material itself out of
        // BomComponentOptions — but this pins the guard that catches it anyway, added straight to BomLineEdits.
        viewModel.BomLineEdits.Add(new IngredientLineRow { IngredientId = viewModel.SelectedRawMaterial!.Id, Grams = 50m });
        viewModel.EditingWeightLossPercent = 25m; // an unrelated edit, so the BOM change isn't what's exercised

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("An ingredient can't be a component of itself.");
        viewModel.SuccessMessage.Should().BeNull("nothing was saved");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(m => m.Name == "Simple Syrup", Cancellation)).WeightLossPercent.Should().Be(20m, "the unrelated edit wasn't saved either");
        (await dbContext.IngredientBomLines.CountAsync(Cancellation)).Should().Be(1, "the bad line was never persisted");
    }

    [Fact]
    public async Task A_made_ingredient_left_at_the_default_weight_loss_saves_with_3_percent()
    {
        await SeedCategoryAsync("Syrups");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Simple Syrup";
        PickCategory(viewModel);
        viewModel.EditingWeightLossPercent.Should().BeNull("a purchased material has no weight loss");
        viewModel.EditingSource = RawMaterialSource.Made;
        viewModel.EditingWeightLossPercent.Should().Be(3m, "a made ingredient starts at 3%");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(Cancellation)).WeightLossPercent.Should().Be(3m);
    }

    [Fact]
    public async Task The_header_shows_a_made_ingredients_yield_and_bom_cost_and_its_buffered_cost_follows_them()
    {
        var sugarId = await SeedCostedIngredientAsync("Sugar", costPerGram: 0.10m, bufferedCostPerGram: 0.11m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Simple Syrup";
        PickCategory(viewModel);
        viewModel.EditingSource = RawMaterialSource.Made;
        viewModel.EditingWeightLossPercent.Should().Be(3m);
        viewModel.BomTotalWeight.Should().Be(0m, "nothing in the BOM yet");
        viewModel.BomTotalCost.Should().Be(0m);

        viewModel.BomLineEdits.Add(new IngredientLineRow { IngredientId = sugarId, Grams = 1000m });

        viewModel.BomTotalWeight.Should().Be(970m, "1,000 g less the 3% lost while making it");
        viewModel.BomTotalCost.Should().Be(110m, "1,000 g of sugar at its buffered ₱0.11");
        viewModel.BufferedCostText.Should().Be("0.1134", "₱110 over the 970 g it yields, as the BOM stands");

        // The weight loss moves the yield, not the cost.
        viewModel.EditingWeightLossPercent = 20m;
        viewModel.BomTotalWeight.Should().Be(800m);
        viewModel.BomTotalCost.Should().Be(110m);
        viewModel.BufferedCostText.Should().Be("0.1375");

        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.SelectedRawMaterial is not null && viewModel.BomLineEdits.Count == 1,
            () => $"selected={viewModel.SelectedRawMaterial?.Name ?? "none"}, lines={viewModel.BomLineEdits.Count}, error={viewModel.ErrorMessage ?? "none"}");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.BomTotalWeight.Should().Be(800m);
        viewModel.BomTotalCost.Should().Be(110m);
        (viewModel.BomTotalCost / viewModel.BomTotalWeight).Should().Be(viewModel.SelectedRawMaterial!.BufferedCostPerBaseUnit,
            "the tiles are the two sides of the buffered cost the cascade saved");
        viewModel.SelectedRawMaterial!.CostBufferPercent.Should().Be(0m, "a made ingredient carries no buffer of its own");
        viewModel.BufferedCostText.Should().Be("0.1375");
    }

    [Fact]
    public async Task A_purchased_materials_header_keeps_showing_its_saved_buffered_cost()
    {
        var flourId = await SeedCostedIngredientAsync("Flour", costPerGram: 0.05m, bufferedCostPerGram: 0.0525m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(m => m.Id == flourId);

        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady, () => "details never arrived");
        viewModel.CurrentCostText.Should().Be("0.0500");
        viewModel.BufferedCostText.Should().Be("0.0525", "only a made ingredient's follows the form");
        viewModel.BomTotalWeight.Should().Be(0m, "it has no BOM");
        viewModel.BomTotalCost.Should().Be(0m);
    }

    [Fact]
    public async Task A_component_repriced_elsewhere_reaches_the_header_totals_on_the_next_load()
    {
        var sugarId = await SeedCostedIngredientAsync("Sugar", costPerGram: 0.10m, bufferedCostPerGram: 0.11m);
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup", (sugarId, 1000m));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(m => m.Id == syrupId);
        await WaitUntilSettledAsync(() => viewModel.BomLineEdits.Count == 1, () => $"lines={viewModel.BomLineEdits.Count}");
        viewModel.BomTotalCost.Should().Be(110m);

        // Sugar goes up behind the screen's back, as another screen or the cascade would leave it.
        await using (var dbContext = CreateDbContext())
        {
            var sugar = await dbContext.RawMaterials.SingleAsync(m => m.Id == sugarId, Cancellation);
            sugar.BufferedCostPerBaseUnit = 0.22m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await viewModel.LoadCommand.Execute();

        viewModel.BomTotalCost.Should().Be(220m, "the refreshed component is what the rows point at");
        viewModel.BomTotalWeight.Should().Be(1000m, "seeded with no weight loss");
        viewModel.BufferedCostText.Should().Be("0.2200");
    }

    [Fact]
    public async Task The_last_updated_tile_is_the_day_the_cost_last_moved_not_the_day_it_was_saved()
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

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(m => m.Id == syrupId);

        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady, () => "details never arrived");
        viewModel.LastUpdatedText.Should().Be("Sep 21, 2026", "the tile is one date, the day its cost changed, with no time");
    }

    [Fact]
    public async Task The_last_updated_tile_falls_back_to_a_dash_for_a_material_whose_cost_has_never_moved()
    {
        var flourId = await SeedPurchasedIngredientAsync("Flour");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(m => m.Id == flourId);

        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady, () => "details never arrived");
        viewModel.LastUpdatedText.Should().Be("—", "it was seeded straight into the database and has never been priced");
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
        AddBrandPrice(viewModel, null, "bag", 90m);
        viewModel.EditingSource = RawMaterialSource.Made;
        viewModel.BomLineEdits.Add(new IngredientLineRow { IngredientId = viewModel.Ingredients.Single().Id, Grams = 100m });

        await viewModel.NewCommand.Execute();

        viewModel.UnitRows.Should().BeEmpty();
        viewModel.CanAddUnit.Should().BeTrue();
        viewModel.PurchaseUnitOptions.Select(o => o.Name).Should().Equal("gram", "bag");
        viewModel.BrandPriceRows.Should().BeEmpty();
        viewModel.PurchaseCostText.Should().Be("—");
        viewModel.BomLineEdits.Should().BeEmpty();
    }

    [Fact]
    public async Task The_usage_example_converts_the_quantity_to_grams_and_costs_it()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var flourId = await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var factory = CreateDbContextFactory();
        var bagUnitId = (await new RawMaterialUnitRepository(factory, CreateCostPropagator()).GetForRawMaterialAsync(flourId, Cancellation)).Single().Id;
        await SetPriceAsync(flourId, bagUnitId, 1250m); // ₱0.05/g

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
    public async Task A_cost_buffer_is_saved_with_the_material_as_a_fixed_buffered_cost_that_prices_dont_move()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingCostBufferPercent.Should().Be(0m, "a new material starts with no buffer");
        viewModel.EditingBufferedCost.Should().BeNull();
        viewModel.EditingName = "Flour";
        PickCategory(viewModel);
        await AddUnitAsync(viewModel, bagId, 25000m);
        var brand = AddBrandPrice(viewModel, null, "bag", 1250m);
        viewModel.TypedCostBufferPercent = 5m;
        viewModel.EditingBufferedCost.Should().Be(0.0525m, "typing the percent puts it on top of the dearest brand");
        viewModel.PurchaseCostText.Should().Be("0.0500");
        viewModel.BufferedCostUnit!.Key.Should().Be(PurchaseUnitChoice.BaseUnitKey, "a new material starts on its base unit");
        viewModel.BufferedCostUnit = viewModel.BufferedCostUnitOptions.Single(o => o.Name == "bag");
        viewModel.PurchaseBufferedCostText.Should().Contain("1,312.50/bag");
        brand.BufferPercent.Should().Be(5m, "the brand's Buffer % column is the buffered cost over its price");

        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.PriceHistory.Count == 1,
            () => $"price rows={viewModel.PriceHistory.Count}, error={viewModel.ErrorMessage ?? "none"}");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SelectedRawMaterial!.CostBufferPercent.Should().Be(5m);
        viewModel.EditingCostBufferPercent.Should().Be(5m, "the form reloads what was saved");
        viewModel.EditingBufferedCost.Should().Be(0.0525m);
        viewModel.BufferedCostUnit!.Name.Should().Be("bag", "a saved material starts on its dearest brand's unit");
        viewModel.EditingBufferedCostInUnit.Should().Be(1312.5m);
        viewModel.HeaderCostUnitText.Should().Be("bag");
        viewModel.CurrentCostText.Should().Be("1,250.00");
        viewModel.BufferedCostText.Should().Be("1,312.50");

        // The header follows the Per unit: per gram, to 4 decimals.
        viewModel.BufferedCostUnit = viewModel.BufferedCostUnitOptions[0];
        viewModel.HeaderCostUnitText.Should().Be("g");
        viewModel.CurrentCostText.Should().Be("0.0500");
        viewModel.BufferedCostText.Should().Be("0.0525");
        viewModel.BufferedCostUnit = viewModel.BufferedCostUnitOptions.Single(o => o.Name == "bag");
        viewModel.BufferedCostWarningText.Should().BeEmpty();

        // The bag goes up to ₱1,500 (₱0.06/g): the buffered cost stays, the % follows it over the new price, and the
        // screen says the buffer's gone.
        viewModel.BrandPriceRows.Single().Price = 1500m;
        viewModel.EditingBufferedCost.Should().Be(0.0525m, "a new price doesn't move the amount");
        viewModel.EditingCostBufferPercent.Should().Be(0m, "₱1,312.50 is under ₱1,500, so the buffer's gone");
        viewModel.BrandPriceRows.Single().BufferPercent.Should().Be(-12.5m, "₱1,312.50 is 12.5% under ₱1,500");
        viewModel.BufferedCostWarningText.Should().NotBeEmpty();

        // Typing the percent works the buffered cost out from the new dearest price.
        viewModel.TypedCostBufferPercent = 10m;
        viewModel.EditingBufferedCost.Should().Be(0.066m, "₱0.06 with 10% on top");
        viewModel.EditingBufferedCostInUnit.Should().Be(1650m);
        viewModel.BufferedCostWarningText.Should().BeEmpty();
        viewModel.BrandPriceRows.Single().BufferPercent.Should().Be(10m);

        // Typing the buffered cost works the percent out: ₱1,800 over ₱1,500 is 20%.
        viewModel.EditingBufferedCostInUnit = 1800m;
        viewModel.EditingBufferedCost.Should().Be(0.072m);
        viewModel.EditingCostBufferPercent.Should().Be(20m);
        viewModel.TypedCostBufferPercent.Should().Be(20m, "the box shows it");

        // A dearer bag leaves the amount alone and works the % out afresh: ₱1,800 over ₱1,600 is 12.5%.
        viewModel.BrandPriceRows.Single().Price = 1600m;
        viewModel.EditingBufferedCostInUnit.Should().Be(1800m);
        viewModel.EditingCostBufferPercent.Should().Be(12.5m);

        // Typing the percent again puts it back on top of the new dearest price.
        viewModel.TypedCostBufferPercent = 20m;
        viewModel.EditingBufferedCostInUnit.Should().Be(1920m, "₱1,600 with 20% on top");

        await viewModel.SaveCommand.Execute();
        await WaitUntilSettledAsync(() => viewModel.BufferedCostText == "1,920.00",
            () => $"buffered={viewModel.BufferedCostText}, error={viewModel.ErrorMessage ?? "none"}");

        await viewModel.NewCommand.Execute();
        viewModel.EditingCostBufferPercent.Should().Be(0m);
        viewModel.EditingBufferedCost.Should().BeNull();
    }

    [Fact]
    public async Task A_kilogram_of_an_ingredient_is_1000_grams_as_its_picked_and_isnt_typed()
    {
        var kilogramId = await SeedUnitOfMeasureAsync("kilogram", "kg");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();

        // In the Alternative Units grid's add row.
        var kilogram = viewModel.NewUnitRow();
        kilogram.UnitOfMeasureId = kilogramId;
        (kilogram.BaseUnitsPerUnit, kilogram.CanTypeMultiplier).Should().Be((1000m, false));
        var bag = viewModel.NewUnitRow();
        bag.UnitOfMeasureId = bagId;
        (bag.BaseUnitsPerUnit, bag.CanTypeMultiplier).Should().Be((0m, true), "a bag's size is the material's own to type");

        // Picked on a Purchase Cost Setup row, which adds the unit.
        var brand = viewModel.NewBrandPriceRow();
        brand.UnitKey = kilogramId;
        brand.Quantity.Should().Be(1000m);
        brand.CanTypeQuantity.Should().BeFalse();
        viewModel.UnitRows.Should().ContainSingle().Which.BaseUnitsPerUnit.Should().Be(1000m);
    }

    [Fact]
    public async Task An_unsaved_materials_buffered_cost_per_base_unit_is_shown_to_four_places()
    {
        // A new material's Per unit starts on the base unit, where the cost is a fraction of a centavo: ₱0.0525/g
        // rounded to the centavo would read as ₱0.05/g, under the percent just typed.
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Flour";
        PickCategory(viewModel);
        await AddUnitAsync(viewModel, bagId, 25000m);
        AddBrandPrice(viewModel, null, "bag", 1250m);
        viewModel.TypedCostBufferPercent = 5m;

        viewModel.BufferedCostUnit!.Key.Should().Be(PurchaseUnitChoice.BaseUnitKey);
        viewModel.EditingBufferedCostInUnit.Should().Be(0.0525m);
        viewModel.PurchaseBufferedCostText.Should().Be("0.0525/g is 5% over the dearest brand's 0.0500/g.");
        viewModel.EditingCostBufferPercent.Should().Be(5m);

        // On the bag it's to the centavo, as before.
        viewModel.BufferedCostUnit = viewModel.BufferedCostUnitOptions.Single(o => o.Name == "bag");
        viewModel.EditingBufferedCostInUnit.Should().Be(1312.5m);
        viewModel.PurchaseBufferedCostText.Should().Be("1,312.50/bag is 5% over the dearest brand's 1,250.00/bag.");
    }

    [Fact]
    public async Task A_saved_buffered_cost_rounded_to_four_places_doesnt_read_as_under_an_unchanged_dearest_brand()
    {
        var boxId = await SeedUnitOfMeasureAsync("box", "box");
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        // ₱170 for a box of 240 g is ₱0.708333…/g to 28 places, which the decimal(28,20) column cuts to 20.
        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Cello Sheet";
        PickCategory(viewModel);
        await AddUnitAsync(viewModel, boxId, 240m);
        AddBrandPrice(viewModel, null, "box", 170m);
        viewModel.BufferedCostWarningText.Should().BeEmpty();

        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.PriceHistory.Count == 1,
            () => $"price rows={viewModel.PriceHistory.Count}, error={viewModel.ErrorMessage ?? "none"}");
        viewModel.ErrorMessage.Should().BeNull();

        // Opened afresh, as the next session does, the form has what the column kept.
        var reopened = CreateViewModel();
        await reopened.LoadCommand.Execute();
        await SelectAsync(reopened, reopened.RawMaterials.Single());
        reopened.EditingBufferedCost.Should().Be(0.70833333333333333333m);
        reopened.DearestPurchaseCost.Should().BeGreaterThan(reopened.EditingBufferedCost!.Value, "the grid works it out to 28 places");
        reopened.BufferedCostWarningText.Should().BeEmpty("the dearest brand hasn't moved, only the stored amount was rounded");

        // A real rise still warns.
        reopened.BrandPriceRows.Single().Price = 171m;
        reopened.BufferedCostWarningText.Should().NotBeEmpty();
    }

    [Fact]
    public async Task The_buffered_cost_is_typed_in_the_picked_unit_kept_per_base_unit_unrounded_and_only_rounded_for_show()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        await AddUnitAsync(viewModel, bagId, 25000m);
        viewModel.BufferedCostUnitOptions.Select(o => o.Name).Should().Equal("gram", "bag");

        viewModel.BufferedCostUnit = viewModel.BufferedCostUnitOptions.Single(o => o.Name == "bag");
        viewModel.EditingBufferedCostInUnit = 1297.37m;
        viewModel.EditingBufferedCost.Should().Be(0.0518948m, "₱1,297.37 over 25,000 g, not rounded");
        viewModel.EditingBufferedCostInUnit.Should().Be(1297.37m);

        viewModel.BufferedCostUnit = viewModel.BufferedCostUnitOptions[0];
        viewModel.EditingBufferedCostInUnit.Should().Be(0.0519m, "shown to 4 decimals on the base unit, where a centavo is too coarse");
        viewModel.EditingBufferedCost.Should().Be(0.0518948m, "which only rounds what's shown");

        // Removing the bag takes the picker back to the base unit.
        viewModel.BufferedCostUnit = viewModel.BufferedCostUnitOptions.Single(o => o.Name == "bag");
        viewModel.SelectedUnitRow = viewModel.UnitRows.Single();
        await viewModel.DeleteUnitCommand.Execute();
        viewModel.BufferedCostUnitOptions.Select(o => o.Name).Should().Equal("gram");
        viewModel.BufferedCostUnit!.Key.Should().Be(PurchaseUnitChoice.BaseUnitKey);
    }

    [Fact]
    public async Task Each_brands_buffer_percent_is_the_buffered_cost_over_its_own_price()
    {
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        var dearer = viewModel.NewBrandPriceRow();
        dearer.BufferPercent.Should().BeNull("there's no price to work a buffer out from");

        dearer.UnitKey = PurchaseUnitChoice.BaseUnitKey;
        dearer.Price = 1m;
        viewModel.BrandPriceRows.Add(dearer);
        dearer.BufferPercent.Should().Be(0m, "no buffer yet");

        viewModel.TypedCostBufferPercent = 5m;
        viewModel.EditingBufferedCost.Should().Be(1.05m);
        dearer.BufferPercent.Should().Be(5m);

        // A cheaper brand is further under the same buffered cost: ₱1.05 over ₱0.84 is 25%.
        var cheaper = AddBrandPrice(viewModel, "Cheap", "gram", 0.84m);
        cheaper.BufferPercent.Should().Be(25m);

        // Typing the buffered cost works the percent out against the dearest brand, and every row follows.
        viewModel.EditingBufferedCostInUnit = 1.2m;
        viewModel.EditingCostBufferPercent.Should().Be(20m);
        dearer.BufferPercent.Should().Be(20m);
        cheaper.BufferPercent.Should().BeApproximately(42.857m, 0.001m, "₱1.20 over ₱0.84");

        dearer.Price = 1.5m;
        viewModel.EditingBufferedCost.Should().Be(1.2m, "a new price doesn't move it");
        dearer.BufferPercent.Should().Be(-20m);
        viewModel.EditingCostBufferPercent.Should().Be(0m, "the % follows the price: ₱1.20 is under ₱1.50, so it pins at 0");

        // The % the amount works out to is held to the 0-100 the box and the validator allow, and the buffered
        // cost itself stays exactly as typed.
        viewModel.EditingBufferedCostInUnit = 1m;
        viewModel.EditingBufferedCost.Should().Be(1m);
        viewModel.EditingCostBufferPercent.Should().Be(0m, "₱1.00 is under the dearest ₱1.50, so the buffer's gone");
        viewModel.BufferedCostWarningText.Should().NotBeEmpty();

        viewModel.EditingBufferedCostInUnit = 4m;
        viewModel.EditingBufferedCost.Should().Be(4m);
        viewModel.EditingCostBufferPercent.Should().Be(100m, "₱4.00 is 166.67% over ₱1.50, more than the box allows");
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
        await SetPriceAsync(flourId, null, 930m);

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single();

        await WaitUntilSettledAsync(() => viewModel.BrandPriceRows.Count == 1,
            () => $"brands={viewModel.BrandPriceRows.Count}");
        viewModel.EditingCostBufferPercent.Should().Be(5m);
        var brand = viewModel.BrandPriceRows.Single();
        (brand.UnitKey, brand.Price, brand.BufferPercent).Should().Be((PurchaseUnitChoice.BaseUnitKey, 930m, 5m));
        viewModel.PurchaseCostText.Should().Be("930.0000");
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
        await SetPriceAsync(flourId, null, 0.05m); // ₱0.05/g, ₱0.0525/g buffered

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
        var boxesId = await AddPackagingCategoryAsync("Boxes");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.IsEditingIngredient.Should().BeTrue("a new material starts as an ingredient");
        viewModel.SavedBaseUnitText.Should().Be("gram (g)", "an ingredient's base unit is always the gram");
        viewModel.CategoryOptions.Where(c => c.Id > 0).Should().NotContain(c => c.Id == boxesId, "an ingredient is offered ingredient categories only");
        viewModel.EditingName = "Cake Box";
        PickCategory(viewModel);
        viewModel.EditingSource = RawMaterialSource.Made;
        viewModel.EditingWeightLossPercent = 5m;

        viewModel.EditingType = RawMaterialType.Packaging;
        viewModel.IsEditingPackaging.Should().BeTrue();
        viewModel.EditingSource.Should().Be(RawMaterialSource.Purchased, "packaging is always purchased");
        viewModel.EditingWeightLossPercent.Should().BeNull();
        viewModel.IsEditingMade.Should().BeFalse();
        viewModel.EditingCategory.Should().BeNull("the ingredient category it had can't hold packaging");
        viewModel.CategoryOptions.Where(c => c.Id > 0).Select(c => c.Id).Should().Equal([boxesId], "packaging is offered packaging categories only");
        viewModel.EditingCategory = viewModel.CategoryOptions.Single(c => c.Id == boxesId);

        viewModel.EditingBaseUnit = viewModel.UnitsOfMeasure.Single(u => u.Id == pieceId);
        viewModel.BaseUnitAbbreviation.Should().Be("pc", "the form names the base unit before the material is saved");
        viewModel.PurchaseUnitOptions.Select(o => o.Name).Should().Equal("piece");
        viewModel.SavedBaseUnitText.Should().Be("—", "the Base Unit tile shows the unit only once it's saved");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        var box = viewModel.RawMaterials.Single(m => m.Name == "Cake Box");
        box.Type.Should().Be(RawMaterialType.Packaging);
        box.BaseUnitId.Should().Be(pieceId);
        viewModel.SelectedRawMaterial.Should().BeSameAs(box, "saving a new material selects it");
        viewModel.CurrentCostText.Should().Be("0.0000");
        viewModel.SavedBaseUnitText.Should().Be("piece (pc)");
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

        await SelectAsync(viewModel, viewModel.RawMaterials.Single());
        AddBrandPrice(viewModel, null, "bag", 1250m);
        viewModel.ExampleRawMaterial.Should().NotBeNull("selecting a material puts it in the usage example");
        viewModel.EditingCostBufferPercent = 5m;
        viewModel.ExampleQuantity = 3m;

        await viewModel.NewCommand.Execute();

        viewModel.EditingName.Should().BeEmpty();
        viewModel.EditingType.Should().Be(RawMaterialType.Ingredient, "most materials are purchased ingredients");
        viewModel.EditingSource.Should().Be(RawMaterialSource.Purchased);
        viewModel.EditingCategory.Should().BeNull("the category isn't guessed");
        viewModel.BrandPriceRows.Should().BeEmpty();
        viewModel.ExampleRawMaterial.Should().BeNull();
        viewModel.ExampleUnit.Should().BeNull();
        viewModel.ExampleUnitOptions.Should().BeEmpty();

        viewModel.PurchaseCostText.Should().Be("—");
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
        var brand = viewModel.NewBrandPriceRow();
        brand.Price = 1250m;
        viewModel.BrandPriceRows.Add(brand);

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("Pick the purchase unit every price is for, or remove the empty row.");
        viewModel.BrandPriceRows.Should().ContainSingle("the edits stay to be fixed");
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

        await SelectAsync(viewModel, viewModel.RawMaterials.Single());
        AddBrandPrice(viewModel, null, "bag", 1250m);

        await viewModel.DeleteCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.RawMaterials.Should().BeEmpty();
        viewModel.EditingName.Should().BeEmpty("the deleted material's details don't stay in the form");
        viewModel.EditingCategory.Should().BeNull();
        viewModel.BrandPriceRows.Should().BeEmpty();
        viewModel.ExampleRawMaterial.Should().BeNull();
        viewModel.CanChangeTypeAndSource.Should().BeTrue("the form is a new material now");
    }

    [Fact]
    public async Task The_category_picker_lists_just_the_categories_with_no_Add_New_row()
    {
        await SeedCategoryAsync("Dry goods");
        await SeedCategoryAsync("Butter");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.CategoryOptions.Select(c => c.Name).Should().Equal("Butter", "Dry goods");
    }

    [Fact]
    public async Task Typing_a_listed_categorys_name_picks_it_whatever_its_case_without_asking()
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

        viewModel.BeginCategoryEdit();
        TypeCategory(viewModel, "  dry GOODS ");
        await viewModel.CommitCategoryTextAsync();

        prompted.Should().BeFalse("it's a category already");
        viewModel.EditingCategory!.Name.Should().Be("Dry goods");
    }

    [Fact]
    public async Task Typing_a_name_that_isnt_a_category_asks_to_add_it_and_files_the_material_under_it()
    {
        var viewModel = CreateViewModel();
        string? promptedWith = null;
        viewModel.PromptForNewCategory.RegisterHandler(interaction =>
        {
            promptedWith = interaction.Input;
            interaction.SetOutput(interaction.Input);
        });
        await viewModel.LoadCommand.Execute();

        viewModel.BeginCategoryEdit();
        TypeCategory(viewModel, " Spices ");
        await viewModel.CommitCategoryTextAsync();

        promptedWith.Should().Be("Spices", "the prompt starts with the name typed, so it needn't be typed again");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Spices was added.", "a toast says so");
        viewModel.EditingCategory!.Name.Should().Be("Spices", "the material is filed under the category just added");
        viewModel.CategorySearchText.Should().Be("Spices");
        viewModel.Categories.Select(c => c.Name).Should().Equal("Spices");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterialCategories.SingleAsync(Cancellation)).Name.Should().Be("Spices");
    }

    [Fact]
    public async Task A_name_changed_in_the_prompt_to_a_listed_category_picks_that_one_rather_than_adding_it_again()
    {
        await SeedCategoryAsync("Spices");
        var viewModel = CreateViewModel();
        viewModel.PromptForNewCategory.RegisterHandler(interaction => interaction.SetOutput("spices"));
        await viewModel.LoadCommand.Execute();

        viewModel.BeginCategoryEdit();
        TypeCategory(viewModel, "Spice");
        await viewModel.CommitCategoryTextAsync();

        viewModel.EditingCategory!.Name.Should().Be("Spices");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.Categories.Should().ContainSingle("nothing was added");
    }

    [Fact]
    public async Task Cancelling_the_new_category_prompt_puts_back_the_category_the_picker_had()
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

        viewModel.BeginCategoryEdit();
        TypeCategory(viewModel, "Spi");
        viewModel.EditingCategory.Should().BeNull("the picker lets go of its pick as a name it doesn't list is typed");
        await viewModel.CommitCategoryTextAsync();

        prompted.Should().BeTrue();
        viewModel.EditingCategory!.Name.Should().Be("Dry goods");
        viewModel.CategorySearchText.Should().Be("Dry goods", "the picker shows the category again, not what was typed");
        viewModel.Categories.Should().ContainSingle("nothing was added");
        viewModel.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Leaving_the_picker_empty_or_on_its_own_category_asks_nothing()
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

        viewModel.BeginCategoryEdit();
        TypeCategory(viewModel, "Dry goods");
        await viewModel.CommitCategoryTextAsync();
        viewModel.EditingCategory!.Name.Should().Be("Dry goods");

        viewModel.BeginCategoryEdit();
        TypeCategory(viewModel, "");
        await viewModel.CommitCategoryTextAsync();

        prompted.Should().BeFalse();
        viewModel.EditingCategory.Should().BeNull("deleting the picker's text leaves the material with no category");
    }

    [Fact]
    public async Task A_unit_typed_into_the_grid_names_itself_and_is_offered_as_a_purchase_unit_straight_away()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bg");
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();

        // The add row's own item, before the grid commits it: it names its unit as soon as one is picked.
        var row = viewModel.NewUnitRow();
        row.UnitOfMeasureId = bagId;
        row.BaseUnitsPerUnit = 25000m;
        (row.UnitName, row.Abbreviation, row.BaseEquivalentText).Should().Be(("bag", "bg", "25,000 g"));

        viewModel.UnitRows.Add(row);
        var brand = AddBrandPrice(viewModel, null, "bag", 1000m);
        brand.Quantity.Should().Be(25000m, "a unit added in the grid can be priced in before Save");
        brand.CanTypeQuantity.Should().BeTrue("it isn't saved yet");

        // A multiplier corrected in the grid shows as the brand's quantity, and reprices it.
        row.BaseUnitsPerUnit = 20000m;
        brand.Quantity.Should().Be(20000m);
        viewModel.PurchaseCostText.Should().Be("0.0500");
    }

    [Theory]
    [InlineData(false, true, "Pick a unit for every alternative unit, or remove the empty row.")]
    [InlineData(true, false, "Enter how many g are in one bag (Alternative Units).")]
    public async Task Save_refuses_a_half_filled_unit_row_and_saves_nothing(bool pickUnit, bool typeMultiplier, string message)
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Flour";
        PickCategory(viewModel);
        var row = viewModel.NewUnitRow();
        if (pickUnit)
        {
            row.UnitOfMeasureId = bagId;
        }

        if (typeMultiplier)
        {
            row.BaseUnitsPerUnit = 25000m;
        }

        viewModel.UnitRows.Add(row);

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be(message);
        viewModel.UnitRows.Should().ContainSingle("the row stays to be fixed");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Save_refuses_the_same_unit_typed_twice()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedCategoryAsync("Dry goods");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Flour";
        PickCategory(viewModel);
        await AddUnitAsync(viewModel, bagId, 25000m);
        await AddUnitAsync(viewModel, bagId, 50000m);

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("This material already has that unit.");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task A_saved_units_multiplier_corrected_in_the_grid_is_saved()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await SelectAsync(viewModel, viewModel.RawMaterials.Single());
        viewModel.UnitRows.Single().BaseUnitsPerUnit = 20000m;
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterialUnits.SingleAsync(Cancellation)).BaseUnitsPerUnit.Should().Be(20000m);
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
            new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
    }

    /// <summary>
    /// Types an alternative unit into the grid's add row as the grid does: it edits the row the screen hands it
    /// (NewUnitRow), then adds it to the rows when it's committed. Only Save writes it.
    /// </summary>
    private static Task AddUnitAsync(RawMaterialsViewModel viewModel, int unitOfMeasureId, decimal baseUnitsPerUnit)
    {
        var row = viewModel.NewUnitRow();
        row.UnitOfMeasureId = unitOfMeasureId;
        row.BaseUnitsPerUnit = baseUnitsPerUnit;
        viewModel.UnitRows.Add(row);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Types a brand into the Purchase Cost Setup grid's add row as the grid does: it edits the row the screen hands it
    /// (NewBrandPriceRow), picking the unit named <paramref name="unitName"/>, then adds it to the rows when it's
    /// committed. Only Save writes it.
    /// </summary>
    private static BrandPriceRow AddBrandPrice(RawMaterialsViewModel viewModel, string? brand, string unitName, decimal price)
    {
        var row = viewModel.NewBrandPriceRow();
        row.Brand = brand;
        row.UnitKey = viewModel.PurchaseUnitOptions.Single(o => o.Name == unitName).Key;
        row.Price = price;
        viewModel.BrandPriceRows.Add(row);
        return row;
    }

    /// <summary>
    /// Types into the Category picker as ComboBoxAdv's Suggest mode does: the text lands, and a name the picker doesn't
    /// list (exactly, whatever its case) lets go of the category it held.
    /// </summary>
    private static void TypeCategory(RawMaterialsViewModel viewModel, string text)
    {
        viewModel.CategorySearchText = text;
        if (!viewModel.CategoryOptions.Any(c => string.Equals(c.Name, text, StringComparison.OrdinalIgnoreCase)))
        {
            viewModel.EditingCategory = null;
        }
    }

    /// <summary>Picks the first category, which a new material starts without.</summary>
    private static void PickCategory(RawMaterialsViewModel viewModel) =>
        viewModel.EditingCategory = viewModel.Categories.First(c => c.Type == viewModel.EditingType);

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

    /// <summary>A made ingredient with a bill of materials: grams of each component.</summary>
    private async Task<int> SeedMadeIngredientAsync(string name, params (int ComponentId, decimal Grams)[] lines)
    {
        await using var dbContext = CreateDbContext();
        var ingredient = new RawMaterial
        {
            Name = name,
            Type = RawMaterialType.Ingredient,
            Category = new RawMaterialCategory { Name = $"{name} goods" },
            Source = RawMaterialSource.Made,
            WeightLossPercent = 0m,
        };
        dbContext.RawMaterials.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);

        dbContext.IngredientBomLines.AddRange(lines.Select(l => new IngredientBomLine { IngredientId = ingredient.Id, ComponentIngredientId = l.ComponentId, Grams = l.Grams }));
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
    /// Picks <paramref name="material"/> as a click in the list does, and waits for its details (price history, brand
    /// prices, BOM), which are read off the UI thread: until they arrive the previous material's show, and Save waits.
    /// </summary>
    private static async Task SelectAsync(RawMaterialsViewModel viewModel, RawMaterial material)
    {
        viewModel.SelectedRawMaterial = material;
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady, () => $"{material.Name}'s details never arrived");
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
