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
/// edits and picker choices.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class IngredientsViewModelRefreshTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task Reloading_keeps_the_selection_and_unsaved_edits_and_adds_a_unit_created_on_another_tab()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await SeedPurchasedIngredientAsync("Flour", bagId);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedIngredient = viewModel.Ingredients.Single();
        viewModel.EditingName = "Flour (unsaved rename)";
        var selected = viewModel.SelectedIngredient;
        var pickedUnit = viewModel.EditingPurchaseUnit;

        await SeedUnitOfMeasureAsync("sack", "sk");
        await viewModel.LoadCommand.Execute();

        // The grid and the picker track their selection by object, so the lists must still hold
        // the very objects that were selected, not fresh copies of them.
        viewModel.Ingredients.Should().ContainSingle().Which.Should().BeSameAs(selected);
        viewModel.SelectedIngredient.Should().BeSameAs(selected);
        viewModel.PurchaseUnits.Should().Contain(unit => ReferenceEquals(unit, pickedUnit));
        viewModel.EditingPurchaseUnit.Should().BeSameAs(pickedUnit);
        viewModel.EditingName.Should().Be("Flour (unsaved rename)");
        viewModel.PurchaseUnits.Select(u => u.Name).Should().Equal("bag", "sack");
    }

    [Fact]
    public async Task Reloading_points_the_selection_and_picker_at_rows_changed_on_another_tab_without_reloading_the_form()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var flourId = await SeedPurchasedIngredientAsync("Flour", bagId);
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
        viewModel.SelectedIngredient.PurchaseUnit!.Abbreviation.Should().Be("bg", "the row shows the renamed unit");
        viewModel.EditingPurchaseUnit!.Abbreviation.Should().Be("bg");
        viewModel.EditingName.Should().Be("Flour (unsaved rename)", "re-pointing the selection must not reload the form");
    }

    private IngredientsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var costPropagator = new IngredientCostPropagator(factory, new SubAssemblyCostPropagator(factory));

        return new IngredientsViewModel(
            new IngredientRepository(factory, costPropagator),
            new IngredientPriceHistoryRepository(factory, costPropagator),
            new IngredientBomLineRepository(factory, costPropagator),
            new IngredientCategoryRepository(factory),
            new IngredientValidator(),
            new IngredientPriceHistoryValidator(),
            new IngredientCategoryValidator(),
            new UnitOfMeasureRepository(factory),
            new LoggerConfiguration().CreateLogger());
    }

    private async Task<int> SeedPurchasedIngredientAsync(string name, int purchaseUnitId)
    {
        await using var dbContext = CreateDbContext();
        var category = new IngredientCategory { Name = "Dry goods" };
        dbContext.IngredientCategories.Add(category);
        var ingredient = new Ingredient
        {
            Name = name,
            IngredientCategory = category,
            Source = IngredientSource.Purchased,
            PurchaseUnitId = purchaseUnitId,
            PurchaseUnitQty = 25000m,
        };
        dbContext.Ingredients.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return ingredient.Id;
    }
}
