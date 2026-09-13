using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Fixtures;
using TinapayanRMS.UI;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.Tests.ViewModels;

/// <summary>
/// Coming back to the Ingredients tab re-runs LoadCommand (MainWindow). It must pick up what other
/// tabs changed while keeping the screen as the user left it: the selected row, unsaved form
/// edits and picker choices. Also covers the unit-aware purchase price and usage example.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class IngredientsViewModelRefreshTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task Reloading_keeps_the_selection_and_unsaved_edits_and_adds_a_unit_of_measure_created_on_another_tab()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedIngredient = viewModel.Ingredients.Single();
        viewModel.EditingName = "Flour (unsaved rename)";
        viewModel.CanAddUnit.Should().BeTrue("one alternative unit is below the limit of two");
        await viewModel.AddUnitCommand.Execute();
        viewModel.EditingUnitOfMeasure = viewModel.UnitsOfMeasure.Single();
        var selected = viewModel.SelectedIngredient;
        var pickedUnit = viewModel.EditingUnitOfMeasure;

        await SeedUnitOfMeasureAsync("sack", "sk");
        await viewModel.LoadCommand.Execute();

        // The grid and the picker track their selection by object, so the lists must still hold
        // the very objects that were selected, not fresh copies of them.
        viewModel.Ingredients.Should().ContainSingle().Which.Should().BeSameAs(selected);
        viewModel.SelectedIngredient.Should().BeSameAs(selected);
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

        viewModel.SelectedIngredient = viewModel.Ingredients.Single();
        viewModel.EditingName = "Flour (unsaved rename)";

        await using (var dbContext = CreateDbContext())
        {
            var bag = await dbContext.UnitsOfMeasure.FindAsync([bagId], Cancellation);
            bag!.Abbreviation = "bg";
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await viewModel.LoadCommand.Execute();

        viewModel.SelectedIngredient!.Id.Should().Be(flourId);
        viewModel.SelectedIngredient.Units.Should().ContainSingle().Which.UnitOfMeasure!.Abbreviation.Should().Be("bg", "the row shows the renamed unit");
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

        viewModel.SelectedIngredient = viewModel.Ingredients.Single();
        await WaitUntilSettledAsync(() => viewModel.PurchaseUnitOptions.Count == 3 && viewModel.EditingPurchaseUnit is not null,
            () => $"options={viewModel.PurchaseUnitOptions.Count}, picked={viewModel.EditingPurchaseUnit?.Name ?? "none"}");
        viewModel.EditingPurchaseUnit!.Name.Should().Be("bag", "an unpriced ingredient starts on its largest unit");
        viewModel.CanAddUnit.Should().BeFalse("the ingredient already has the two alternative units allowed");
        (await viewModel.AddUnitCommand.CanExecute.FirstAsync()).Should().BeFalse();

        viewModel.EditingPurchaseUnit = viewModel.PurchaseUnitOptions.Single(o => o.Name == "pack");
        viewModel.EditingPrice = 60m;
        viewModel.PurchaseCostPerGram.Should().Be(0.06m, "the cost per gram is worked out as the price is typed");
        await viewModel.SavePriceCommand.Execute();

        await WaitUntilSettledAsync(() => viewModel.PriceHistory.Count == 1,
            () => $"price rows={viewModel.PriceHistory.Count}, error={viewModel.ErrorMessage ?? "none"}");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.PriceHistory.Single().UnitName.Should().Be("pack");
        viewModel.PriceHistory.Single().CostPerGram.Should().Be(0.06m);
        viewModel.SelectedIngredient!.CostPerGram.Should().Be(0.06m);
        viewModel.EditingPurchaseUnit!.Name.Should().Be("pack", "the setup shows the latest price's unit");
        viewModel.EditingPrice.Should().Be(60m);
    }

    [Fact]
    public async Task The_usage_example_converts_the_quantity_to_grams_and_costs_it()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var flourId = await SeedPurchasedIngredientAsync("Flour", (bagId, 25000m));
        var factory = CreateDbContextFactory();
        var costPropagator = new IngredientCostPropagator(factory, new SubAssemblyCostPropagator(factory));
        var bagUnitId = (await new IngredientUnitRepository(factory, costPropagator).GetForIngredientAsync(flourId, Cancellation)).Single().Id;
        await new IngredientPriceHistoryRepository(factory, costPropagator).AddAsync(
            new IngredientPriceHistory { IngredientId = flourId, IngredientUnitId = bagUnitId, Date = new DateOnly(2026, 9, 1), Price = 1250m },
            Cancellation); // ₱0.05/g

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.ExampleIngredient = viewModel.Ingredients.Single();
        viewModel.ExampleQuantity = 2m;
        viewModel.ExampleUnit = viewModel.ExampleUnitOptions.Single(o => o.Name == "bag");

        await viewModel.CalculateExampleCommand.Execute();

        viewModel.ExampleResult.Should().NotBeNull();
        viewModel.ExampleResult!.Title.Should().Be("Cost for 2 bag of Flour");
        viewModel.ExampleResult.QuantityInGrams.Should().Be(50000m);
        viewModel.ExampleResult.CostPerGram.Should().Be(0.05m);
        viewModel.ExampleResult.TotalCost.Should().Be(2500m);
    }

    private IngredientsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var costPropagator = new IngredientCostPropagator(factory, new SubAssemblyCostPropagator(factory));

        return new IngredientsViewModel(
            new IngredientRepository(factory, costPropagator),
            new IngredientUnitRepository(factory, costPropagator),
            new IngredientPriceHistoryRepository(factory, costPropagator),
            new IngredientBomLineRepository(factory, costPropagator),
            new IngredientCategoryRepository(factory),
            new IngredientValidator(),
            new IngredientUnitValidator(),
            new IngredientPriceHistoryValidator(),
            new IngredientCategoryValidator(),
            new UnitOfMeasureRepository(factory),
            new LoggerConfiguration().CreateLogger());
    }

    private async Task<int> SeedPurchasedIngredientAsync(string name, params (int UnitOfMeasureId, decimal GramsPerUnit)[] units)
    {
        await using var dbContext = CreateDbContext();
        var category = new IngredientCategory { Name = "Dry goods" };
        dbContext.IngredientCategories.Add(category);
        var ingredient = new Ingredient
        {
            Name = name,
            IngredientCategory = category,
            Source = IngredientSource.Purchased,
            Units = [.. units.Select(u => new IngredientUnit { UnitOfMeasureId = u.UnitOfMeasureId, GramsPerUnit = u.GramsPerUnit })],
        };
        dbContext.Ingredients.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return ingredient.Id;
    }

    /// <summary>
    /// Selecting an ingredient loads its price history in the background (the ViewModel's selection
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

        throw new TimeoutException($"The ingredient's details never finished loading: {describe()}.");
    }
}
