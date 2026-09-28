using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class IngredientBomRulesTests
{
    [Fact]
    public void EnsureValidLines_rejects_an_empty_list()
    {
        var act = () => IngredientBomRules.EnsureValidLines([]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidLines_rejects_non_positive_grams()
    {
        var act = () => IngredientBomRules.EnsureValidLines([(ComponentIngredientId: 1, Grams: 0m)]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidLines_rejects_the_same_component_twice()
    {
        var act = () => IngredientBomRules.EnsureValidLines(
        [
            (ComponentIngredientId: 1, Grams: 100m),
            (ComponentIngredientId: 1, Grams: 50m),
        ]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidLines_accepts_distinct_positive_lines()
    {
        var act = () => IngredientBomRules.EnsureValidLines(
        [
            (ComponentIngredientId: 1, Grams: 1000m),
            (ComponentIngredientId: 2, Grams: 500m),
        ]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureNoCycle_rejects_an_ingredient_as_its_own_component()
    {
        var act = () => IngredientBomRules.EnsureNoCycle(ingredientId: 1, newComponentIds: [1], componentIdsByIngredientId: new Dictionary<int, IReadOnlyList<int>>());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureNoCycle_rejects_a_component_that_already_contains_the_ingredient()
    {
        // Ingredient 2 (syrup) is already made from ingredient 1 (sugar). Trying to make
        // ingredient 1 out of ingredient 2 would create a 1 -> 2 -> 1 cycle.
        var componentIdsByIngredientId = new Dictionary<int, IReadOnlyList<int>>
        {
            [2] = [1],
        };

        var act = () => IngredientBomRules.EnsureNoCycle(ingredientId: 1, newComponentIds: [2], componentIdsByIngredientId);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureNoCycle_rejects_a_cycle_hidden_several_levels_deep()
    {
        // 4 (caramel) is made from 3 (syrup), which is made from 2, which is made from 1
        // (sugar). Trying to make 1 out of 4 would create a cycle several levels down.
        var componentIdsByIngredientId = new Dictionary<int, IReadOnlyList<int>>
        {
            [4] = [3],
            [3] = [2],
            [2] = [1],
        };

        var act = () => IngredientBomRules.EnsureNoCycle(ingredientId: 1, newComponentIds: [4], componentIdsByIngredientId);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureNoCycle_accepts_an_unrelated_component()
    {
        var componentIdsByIngredientId = new Dictionary<int, IReadOnlyList<int>>
        {
            [2] = [1],
        };

        var act = () => IngredientBomRules.EnsureNoCycle(ingredientId: 3, newComponentIds: [2], componentIdsByIngredientId);

        act.Should().NotThrow();
    }

    [Fact]
    public void CalculateCost_applies_weight_loss_and_weighted_average_cost()
    {
        // 1000g sugar @ ₱0.09/g (₱90) + 500g water @ ₱0/g, 20% weight loss.
        var (outputGrams, costPerGram) = IngredientBomRules.CalculateCost(
            [(Grams: 1000m, ComponentCostPerGram: 0.09m), (Grams: 500m, ComponentCostPerGram: 0m)],
            weightLossPercent: 20m);

        outputGrams.Should().Be(1200m);
        costPerGram.Should().Be(0.075m);
    }

    [Fact]
    public void CalculateCost_with_zero_weight_loss_keeps_the_full_input_weight()
    {
        var (outputGrams, costPerGram) = IngredientBomRules.CalculateCost(
            [(Grams: 100m, ComponentCostPerGram: 1m)],
            weightLossPercent: 0m);

        outputGrams.Should().Be(100m);
        costPerGram.Should().Be(1m);
    }

    [Fact]
    public void CalculateCost_with_everything_boiled_away_returns_zero_cost_per_gram_instead_of_dividing_by_zero()
    {
        // 100% weight loss: output is 0g, so CostPerBaseUnit falls back to 0 rather than a
        // divide-by-zero. RawMaterialRules.EnsureFieldsMatchSource rejects a weight loss
        // percent of 100 at the data-entry layer, but CalculateCost is a pure function
        // with no such guard of its own, so this defensive branch is exercised directly.
        var (outputGrams, costPerGram) = IngredientBomRules.CalculateCost(
            [(Grams: 1000m, ComponentCostPerGram: 0.09m)],
            weightLossPercent: 100m);

        outputGrams.Should().Be(0m);
        costPerGram.Should().Be(0m);
    }
}
