using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
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
/// edits and picker choices. Also covers the unit-aware purchase price, the usage example, and how a
/// new material's type decides which of its fields apply.
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

        await using (var dbContext = CreateDbContext())
        {
            var bag = await dbContext.UnitsOfMeasure.FindAsync([bagId], Cancellation);
            bag!.Abbreviation = "bg";
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRawMaterial!.Id.Should().Be(flourId);
        viewModel.SelectedRawMaterial.Units.Should().ContainSingle().Which.UnitOfMeasure!.Abbreviation.Should().Be("bg", "the row shows the renamed unit");
        viewModel.UnitRows.Should().ContainSingle().Which.Example.Should().Be("1 bg = 25,000 g");
        viewModel.EditingName.Should().Be("Flour (unsaved rename)", "re-pointing the selection must not reload the form");
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
        viewModel.PurchaseCostText.Should().Be("₱0.0600 / g", "the cost per gram is worked out as the price is typed");
        await viewModel.SavePriceCommand.Execute();

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
    public async Task A_new_material_switched_to_packaging_drops_the_ingredient_only_fields_and_saves_its_own_base_unit()
    {
        await SeedPurchasedIngredientAsync("Flour");
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.IsEditingIngredient.Should().BeTrue("a new material starts as an ingredient");
        viewModel.EditingName = "Cake Box";
        viewModel.EditingSource = RawMaterialSource.Made;
        viewModel.EditingWeightLossPercent = 5m;

        viewModel.EditingType = RawMaterialType.Packaging;
        viewModel.IsEditingPackaging.Should().BeTrue();
        viewModel.EditingSource.Should().Be(RawMaterialSource.Purchased, "packaging is always purchased");
        viewModel.EditingWeightLossPercent.Should().BeNull();
        viewModel.IsEditingMade.Should().BeFalse();

        viewModel.EditingBaseUnit = viewModel.UnitsOfMeasure.Single(u => u.Id == pieceId);
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        var box = viewModel.RawMaterials.Single(m => m.Name == "Cake Box");
        box.Type.Should().Be(RawMaterialType.Packaging);
        box.BaseUnitId.Should().Be(pieceId);
        viewModel.SelectedRawMaterial.Should().BeSameAs(box, "saving a new material selects it");
        viewModel.CurrentCostText.Should().Be("₱0.0000 / pc", "its cost is per its own base unit");
        viewModel.CanChangeTypeAndSource.Should().BeFalse("the type is fixed once saved");
        viewModel.Ingredients.Should().NotContain(box, "the BOM component picker lists ingredients only");
    }

    private RawMaterialsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var costPropagator = CreateCostPropagator();

        return new RawMaterialsViewModel(
            new RawMaterialRepository(factory, costPropagator),
            new RawMaterialUnitRepository(factory, costPropagator),
            new RawMaterialPriceHistoryRepository(factory, costPropagator),
            new IngredientBomLineRepository(factory, costPropagator),
            new RawMaterialCategoryRepository(factory),
            new RawMaterialValidator(),
            new RawMaterialUnitValidator(),
            new RawMaterialPriceHistoryValidator(),
            new UnitOfMeasureRepository(factory),
            new LoggerConfiguration().CreateLogger());
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
