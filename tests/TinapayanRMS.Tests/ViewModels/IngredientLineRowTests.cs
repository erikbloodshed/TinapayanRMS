using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.UI;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.Tests.ViewModels;

public class IngredientLineRowTests
{
    public IngredientLineRowTests() => ReactiveUIBootstrapper.EnsureInitialized();

    private static readonly RawMaterial Flour = new() { Id = 1, Name = "Flour", BufferedCostPerBaseUnit = 0.05m };
    private static readonly RawMaterial Sugar = new() { Id = 2, Name = "Sugar", BufferedCostPerBaseUnit = 0.09m };

    private static readonly Dictionary<int, RawMaterial> IngredientsById = new() { [Flour.Id] = Flour, [Sugar.Id] = Sugar };

    [Fact]
    public void Resolve_points_a_picked_row_at_its_ingredient_which_prices_it()
    {
        var row = new IngredientLineRow { IngredientId = Sugar.Id, Grams = 200m };

        IngredientLineRow.Resolve([row], IngredientsById);

        row.Ingredient.Should().BeSameAs(Sugar);
        row.Cost.Should().Be(18m);
    }

    [Fact]
    public void Resolve_swaps_in_the_refreshed_copy_of_an_ingredient()
    {
        var row = new IngredientLineRow(Flour, 1000m);
        var repriced = new RawMaterial { Id = Flour.Id, Name = "Flour", BufferedCostPerBaseUnit = 0.06m };

        IngredientLineRow.Resolve([row], new Dictionary<int, RawMaterial> { [Flour.Id] = repriced });

        row.Ingredient.Should().BeSameAs(repriced);
        row.Cost.Should().Be(60m);
    }

    [Fact]
    public void Resolve_keeps_an_ingredient_the_screen_no_longer_lists()
    {
        var row = new IngredientLineRow(Flour, 1000m);

        IngredientLineRow.Resolve([row], new Dictionary<int, RawMaterial>());

        row.Ingredient.Should().BeSameAs(Flour);
    }

    [Fact]
    public void CompleteLines_leaves_out_a_blank_add_row()
    {
        var lines = IngredientLineRow.CompleteLines([new IngredientLineRow(Flour, 1000m), new IngredientLineRow()], "Bread Dough");

        lines.Should().Equal((Flour, 1000m));
    }

    [Fact]
    public void CompleteLines_refuses_grams_without_an_ingredient()
    {
        var act = () => IngredientLineRow.CompleteLines([new IngredientLineRow { Grams = 150m }], "Bread Dough");

        act.Should().Throw<InvalidOperationException>().WithMessage("Pick the ingredient for the 150 g line of Bread Dough, or delete that line.");
    }

    [Fact]
    public void CompleteLines_refuses_an_ingredient_without_grams()
    {
        var row = new IngredientLineRow { IngredientId = Sugar.Id };
        IngredientLineRow.Resolve([row], IngredientsById);

        var act = () => IngredientLineRow.CompleteLines([row], "Bread Dough");

        act.Should().Throw<InvalidOperationException>().WithMessage("Enter how many grams of Sugar go into Bread Dough, or delete its line.");
    }

    [Fact]
    public void CompleteLines_refuses_an_ingredient_on_two_rows()
    {
        var act = () => IngredientLineRow.CompleteLines([new IngredientLineRow(Sugar, 100m), new IngredientLineRow(Sugar, 50m)], "Bread Dough");

        act.Should().Throw<InvalidOperationException>().WithMessage("Sugar is on two lines of Bread Dough. Put its total on one line and delete the other.");
    }
}
