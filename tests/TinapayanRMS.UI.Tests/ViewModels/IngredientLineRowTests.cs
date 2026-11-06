using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

public class IngredientLineRowTests
{
    public IngredientLineRowTests() => ReactiveUIBootstrapper.EnsureInitialized();

    private static readonly RawMaterial Flour = new() { Id = 1, Name = "Flour", BufferedCostPerBaseUnit = 0.05m };
    private static readonly RawMaterial Sugar = new() { Id = 2, Name = "Sugar", BufferedCostPerBaseUnit = 0.09m };

    private static readonly Dictionary<int, RawMaterial> IngredientsById = new() { [Flour.Id] = Flour, [Sugar.Id] = Sugar };

    [Test]
    public void Switched_to_unbuffered_a_row_is_priced_at_its_ingredients_true_cost()
    {
        var butter = new RawMaterial { Id = 3, Name = "Butter", CostPerBaseUnit = 0.40m, BufferedCostPerBaseUnit = 0.44m };
        var row = new IngredientLineRow(butter, 100m);
        row.CostBasis.Should().Be(CostBasis.Buffered);
        (row.CostPerGram, row.Cost).Should().Be((0.44m, 44m));
        List<string?> changed = [];
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.CostBasis = CostBasis.Unbuffered;

        (row.CostPerGram, row.Cost).Should().Be((0.40m, 40m));
        changed.Should().Contain([nameof(IngredientLineRow.CostPerGram), nameof(IngredientLineRow.Cost)]);
    }

    [Test]
    public void Resolve_points_a_picked_row_at_its_ingredient_which_prices_it()
    {
        var row = new IngredientLineRow { IngredientId = Sugar.Id, Grams = 200m };

        IngredientLineRow.Resolve([row], IngredientsById);

        row.Ingredient.Should().BeSameAs(Sugar);
        row.Cost.Should().Be(18m);
    }

    [Test]
    public void Resolve_swaps_in_the_refreshed_copy_of_an_ingredient()
    {
        var row = new IngredientLineRow(Flour, 1000m);
        var repriced = new RawMaterial { Id = Flour.Id, Name = "Flour", BufferedCostPerBaseUnit = 0.06m };

        IngredientLineRow.Resolve([row], new Dictionary<int, RawMaterial> { [Flour.Id] = repriced });

        row.Ingredient.Should().BeSameAs(repriced);
        row.Cost.Should().Be(60m);
    }

    [Test]
    public void Resolve_keeps_an_ingredient_the_screen_no_longer_lists()
    {
        var row = new IngredientLineRow(Flour, 1000m);

        IngredientLineRow.Resolve([row], new Dictionary<int, RawMaterial>());

        row.Ingredient.Should().BeSameAs(Flour);
    }

    [Test]
    public void A_new_row_with_a_lookup_prices_itself_as_its_ingredient_and_grams_are_entered()
    {
        var row = new IngredientLineRow(id => IngredientsById.GetValueOrDefault(id));
        List<string> changed = [];
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        row.IngredientId = Sugar.Id;
        row.Ingredient.Should().BeSameAs(Sugar, "the add row isn't in the screen's lines yet, so it finds its own ingredient");
        row.Grams = 200m;

        row.Cost.Should().Be(18m);
        changed.Should().Contain(nameof(IngredientLineRow.Cost), "the grid's Cost cell updates from it");

        row.IngredientId = null;
        row.Ingredient.Should().BeNull();
        row.Cost.Should().Be(0m);
    }

    [Test]
    public void A_new_row_leaves_its_grams_and_percentage_empty_until_typed()
    {
        var row = new IngredientLineRow(id => IngredientsById.GetValueOrDefault(id), baseWeight: 1000m) { IngredientId = Sugar.Id };

        row.Grams.Should().BeNull("the grid shows an empty cell rather than 0.00 (AllowNullValue)");
        row.Percent.Should().BeNull();
        row.Cost.Should().Be(0m);

        row.Grams = 200m;
        row.Grams = null;

        row.Grams.Should().BeNull("clearing the cell leaves it empty");
        row.Percent.Should().BeNull();
    }

    [Test]
    public void CompleteLines_leaves_out_a_blank_add_row()
    {
        var lines = IngredientLineRow.CompleteLines([new IngredientLineRow(Flour, 1000m), new IngredientLineRow()], "Bread Dough");

        lines.Should().Equal((Flour, 1000m, false, (decimal?)null));
    }

    [Test]
    public void CompleteLines_refuses_grams_without_an_ingredient()
    {
        var act = () => IngredientLineRow.CompleteLines([new IngredientLineRow { Grams = 150m }], "Bread Dough");

        act.Should().Throw<InvalidOperationException>().WithMessage("Pick the ingredient for the 150 g line of Bread Dough, or delete that line.");
    }

    [Test]
    public void CompleteLines_refuses_an_ingredient_without_grams()
    {
        var row = new IngredientLineRow { IngredientId = Sugar.Id };
        IngredientLineRow.Resolve([row], IngredientsById);

        var act = () => IngredientLineRow.CompleteLines([row], "Bread Dough");

        act.Should().Throw<InvalidOperationException>().WithMessage("Enter how many grams of Sugar go into Bread Dough, or delete its line.");
    }

    [Test]
    public void CompleteLines_refuses_grams_cleared_back_to_empty()
    {
        var row = new IngredientLineRow(Sugar, 100m) { Grams = null };

        var act = () => IngredientLineRow.CompleteLines([row], "Bread Dough");

        act.Should().Throw<InvalidOperationException>().WithMessage("Enter how many grams of Sugar go into Bread Dough, or delete its line.");
    }

    [Test]
    public void CompleteLines_refuses_an_ingredient_on_two_rows()
    {
        var act = () => IngredientLineRow.CompleteLines([new IngredientLineRow(Sugar, 100m), new IngredientLineRow(Sugar, 50m)], "Bread Dough");

        act.Should().Throw<InvalidOperationException>().WithMessage("Sugar is on two lines of Bread Dough. Put its total on one line and delete the other.");
    }

    /// <summary>A 1,000 g flour base, then water given as 65% and eggs given as 100 g, the base worked out across them.</summary>
    private static (IngredientLineRow Flour, IngredientLineRow Water, IngredientLineRow Eggs, List<IngredientLineRow> Rows) DoughWithTypedFigures()
    {
        var eggs = new RawMaterial { Id = 3, Name = "Eggs", BufferedCostPerBaseUnit = 0.1m };
        var flour = new IngredientLineRow(Flour, 1000m, isFormulaBase: true);
        var water = new IngredientLineRow(Sugar, 0m);
        var eggRow = new IngredientLineRow(eggs, 100m);
        List<IngredientLineRow> rows = [flour, water, eggRow];
        IngredientLineRow.ApplyFormulaBase(rows);
        water.Percent = 65m;
        return (flour, water, eggRow, rows);
    }

    [Test]
    public void Typing_a_percentage_works_out_the_grams_and_keeps_the_percentage()
    {
        var (_, water, _, _) = DoughWithTypedFigures();

        water.Grams.Should().Be(650m);
        water.Percent.Should().Be(65m);
        water.IsPercentTyped.Should().BeTrue();
        water.AuthoredBakersPercent.Should().Be(65m);
    }

    [Test]
    public void Typing_grams_lets_a_typed_percentage_go()
    {
        var (_, water, _, _) = DoughWithTypedFigures();

        water.Grams = 700m;

        water.IsPercentTyped.Should().BeFalse("the grams are now the figure that was typed");
        water.Percent.Should().Be(70m);
    }

    [Test]
    public void A_line_typed_as_a_percentage_follows_the_base_and_one_typed_in_grams_doesnt()
    {
        var (flour, water, eggs, rows) = DoughWithTypedFigures();

        flour.Grams = 2000m;
        IngredientLineRow.ApplyFormulaBase(rows);

        water.Grams.Should().Be(1300m, "65% was typed, so it stays 65% of the bigger base");
        water.Percent.Should().Be(65m);
        eggs.Grams.Should().Be(100m, "100 g was typed, so it stays 100 g");
        eggs.Percent.Should().Be(5m);
    }

    [Test]
    public void Ticking_a_line_typed_as_a_percentage_makes_it_a_base_line_given_in_grams()
    {
        var (_, water, _, rows) = DoughWithTypedFigures();

        water.IsFormulaBase = true;
        IngredientLineRow.ApplyFormulaBase(rows);

        water.IsPercentTyped.Should().BeFalse("a base line is 100% between its lines, and given in grams");
        water.Grams.Should().Be(650m, "ticking it changes what the percentages are read against, not how much there is");
    }

    [Test]
    public void A_percentage_cant_be_typed_on_a_base_line_or_without_a_base()
    {
        var (flour, _, _, _) = DoughWithTypedFigures();
        flour.CanTypeBakersPercent.Should().BeFalse("the base is 100% by definition");

        var loose = new IngredientLineRow(Sugar, 200m);
        IngredientLineRow.ApplyFormulaBase([loose]);
        loose.CanTypeBakersPercent.Should().BeFalse("nothing is ticked, so there's no base to take a share of");

        loose.Percent = 50m;
        loose.Grams.Should().Be(200m, "without a base the absolute percentage is only a reading of the grams");
        loose.IsPercentTyped.Should().BeFalse();
        loose.Percent.Should().Be(100m, "the only line is the whole batch");
    }

    [Test]
    public void With_no_base_each_line_reads_as_its_share_of_the_whole_batch()
    {
        var butter = new IngredientLineRow(Flour, 150m);
        var sugar = new IngredientLineRow(Sugar, 850m);
        List<IngredientLineRow> rows = [butter, sugar];

        IngredientLineRow.ApplyFormulaBase(rows).Should().Be(0m);

        butter.Percent.Should().Be(15m);
        sugar.Percent.Should().Be(85m);
        IngredientLineRow.PercentHeader(0m).Should().Be("Absolute %");
        IngredientLineRow.PercentHeader(1000m).Should().Be("Baker's %");
    }

    [Test]
    public void With_no_base_a_typed_percentage_keeps_its_grams_and_comes_back_with_the_base()
    {
        var (flour, water, _, rows) = DoughWithTypedFigures();

        flour.IsFormulaBase = false;
        IngredientLineRow.ApplyFormulaBase(rows);
        water.Grams.Should().Be(650m, "with no base, it keeps its last grams");
        water.Percent.Should().Be(650m / 1750m * 100m, "with no base it reads as its share of the 1,750 g batch");
        water.IsPercentTyped.Should().BeFalse("the absolute percentage showing wasn't typed");
        water.Percent = null;
        water.AuthoredBakersPercent.Should().Be(65m, "nothing can be typed without a base, so the typed 65% isn't cleared either");

        flour.IsFormulaBase = true;
        flour.Grams = 2000m;
        IngredientLineRow.ApplyFormulaBase(rows);
        water.Grams.Should().Be(1300m, "its 65% waited for a base to come back");
    }

    [Test]
    public void Restore_puts_back_a_typed_percentage_that_is_waiting_for_a_base()
    {
        // The state the public setters can't express: a percentage was typed, then the base was unticked, so
        // BaseWeight is 0 and the Percent setter refuses to take one. An undo restoring through the setters
        // would drop the 65% silently, and only ticking the base again would show it had gone.
        var row = IngredientLineRow.Restore(
            id => IngredientsById.GetValueOrDefault(id), baseWeight: 0m,
            ingredientId: Sugar.Id, grams: 650m, isFormulaBase: false, authoredBakersPercent: 65m);

        row.AuthoredBakersPercent.Should().Be(65m);
        row.CanTypeBakersPercent.Should().BeFalse("there's no base to read a percentage against");
        row.Grams.Should().Be(650m);

        // With a base back, the percentage it was holding is what works out its grams again.
        var flour = new IngredientLineRow(Flour, 2000m, isFormulaBase: true);
        IngredientLineRow.ApplyFormulaBase([flour, row]);
        row.Grams.Should().Be(1300m, "its 65% waited for a base to come back");
    }

    [Test]
    public void Restore_keeps_a_row_that_is_only_half_filled_in()
    {
        // An ingredient picked and no grams typed yet is a legitimate row (CompleteLines exists for it), and
        // it can't go through the constructor that takes a RawMaterial and a decimal.
        var row = IngredientLineRow.Restore(
            id => IngredientsById.GetValueOrDefault(id), baseWeight: 0m,
            ingredientId: Flour.Id, grams: null, isFormulaBase: false, authoredBakersPercent: null);

        row.IngredientId.Should().Be(Flour.Id);
        row.Ingredient.Should().BeSameAs(Flour, "it resolves its ingredient as it's rebuilt");
        row.Grams.Should().BeNull();
        row.Cost.Should().Be(0m);
    }

    [Test]
    public void Restore_keeps_a_row_with_no_ingredient_at_all()
    {
        var row = IngredientLineRow.Restore(
            id => IngredientsById.GetValueOrDefault(id), baseWeight: 0m,
            ingredientId: null, grams: 120m, isFormulaBase: false, authoredBakersPercent: null);

        row.IngredientId.Should().BeNull();
        row.Ingredient.Should().BeNull();
        row.Grams.Should().Be(120m);
    }

    [Test]
    public void Restore_keeps_the_base_tick_without_losing_the_grams()
    {
        var row = IngredientLineRow.Restore(
            id => IngredientsById.GetValueOrDefault(id), baseWeight: 1000m,
            ingredientId: Flour.Id, grams: 1000m, isFormulaBase: true, authoredBakersPercent: null);

        row.IsFormulaBase.Should().BeTrue();
        row.Grams.Should().Be(1000m);
        row.CanTypeBakersPercent.Should().BeFalse("a base line is 100% by definition");
    }

    [Test]
    public void A_restored_row_prices_itself_when_its_ingredient_is_picked_again()
    {
        var row = IngredientLineRow.Restore(
            id => IngredientsById.GetValueOrDefault(id), baseWeight: 0m,
            ingredientId: null, grams: 100m, isFormulaBase: false, authoredBakersPercent: null);

        row.IngredientId = Flour.Id;

        row.Ingredient.Should().BeSameAs(Flour);
        row.Cost.Should().Be(100m * Flour.BufferedCostPerBaseUnit);
    }
}
