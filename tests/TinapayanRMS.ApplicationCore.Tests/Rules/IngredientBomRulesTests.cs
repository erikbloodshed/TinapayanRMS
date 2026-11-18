using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class IngredientBomRulesTests
{
    [Test]
    public void EnsureValidAddIns_accepts_none()
    {
        var act = () => IngredientBomRules.EnsureValidAddIns([], "Butter Icing Base", [1, 2]);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureValidAddIns_rejects_non_positive_grams()
    {
        var act = () => IngredientBomRules.EnsureValidAddIns([(IngredientId: 3, Grams: 0m, IngredientName: "Ube Flavor")], "Butter Icing Base", [1, 2]);

        act.Should().Throw<BusinessRuleException>().WithMessage("An add-in's grams must be positive.");
    }

    [Test]
    public void EnsureValidAddIns_rejects_the_same_ingredient_twice()
    {
        var act = () => IngredientBomRules.EnsureValidAddIns(
        [
            (IngredientId: 3, Grams: 10m, IngredientName: "Ube Flavor"),
            (IngredientId: 3, Grams: 5m, IngredientName: "Ube Flavor"),
        ], "Butter Icing Base", [1, 2]);

        act.Should().Throw<BusinessRuleException>().WithMessage("Ube Flavor is on two add-in lines*");
    }

    [Test]
    public void EnsureValidAddIns_rejects_an_ingredient_the_recipe_already_has()
    {
        var act = () => IngredientBomRules.EnsureValidAddIns([(IngredientId: 2, Grams: 10m, IngredientName: "Sugar")], "Butter Icing Base", [1, 2]);

        act.Should().Throw<BusinessRuleException>().WithMessage("Sugar is already in Butter Icing Base*Sub-Assemblies screen*");
    }

    [Test]
    public void EnsureNotInOwnRecipe_rejects_the_made_ingredient_as_a_line_of_its_sub_assembly()
    {
        var act = () => IngredientBomRules.EnsureNotInOwnRecipe(5, "Butter Icing", "Butter Icing Base", [1, 5]);

        act.Should().Throw<BusinessRuleException>().WithMessage("Butter Icing is made from Butter Icing Base, so it can't be one of Butter Icing Base's ingredients.");
    }

    private static readonly Dictionary<int, string> Names = new()
    {
        [1] = "Sugar",
        [2] = "Simple Syrup",
        [3] = "Glaze",
        [4] = "Caramel",
    };

    private static readonly Func<int, string> NameOf = id => Names[id];

    [Test]
    public void EnsureNoCycle_rejects_an_ingredient_as_its_own_component()
    {
        var act = () => IngredientBomRules.EnsureNoCycle(
            ingredientId: 1, newComponentIds: [1], componentIdsByIngredientId: new Dictionary<int, IReadOnlyList<int>>(), NameOf);

        act.Should().Throw<BusinessRuleException>().WithMessage("An ingredient can't be a component of itself.");
    }

    [Test]
    public void EnsureNoCycle_rejects_a_component_that_already_contains_the_ingredient_by_name()
    {
        // Ingredient 2 (syrup) is already made from ingredient 1 (sugar). Trying to make
        // ingredient 1 out of ingredient 2 would create a 1 -> 2 -> 1 cycle.
        var componentIdsByIngredientId = new Dictionary<int, IReadOnlyList<int>>
        {
            [2] = [1],
        };

        var act = () => IngredientBomRules.EnsureNoCycle(ingredientId: 1, newComponentIds: [2], componentIdsByIngredientId, NameOf);

        act.Should().Throw<BusinessRuleException>().WithMessage(
            "Sugar can't be costed from Simple Syrup: Simple Syrup is already costed from Sugar, through a recipe or a link, so Sugar would end up costed from itself.");
    }

    [Test]
    public void EnsureNoCycle_rejects_a_cycle_hidden_several_levels_deep()
    {
        // 4 (caramel) is made from 3 (glaze), which is made from 2 (simple syrup), which is made from 1
        // (sugar). Trying to make 1 out of 4 would create a cycle several levels down.
        var componentIdsByIngredientId = new Dictionary<int, IReadOnlyList<int>>
        {
            [4] = [3],
            [3] = [2],
            [2] = [1],
        };

        var act = () => IngredientBomRules.EnsureNoCycle(ingredientId: 1, newComponentIds: [4], componentIdsByIngredientId, NameOf);

        act.Should().Throw<BusinessRuleException>().WithMessage("Sugar can't be costed from Caramel: Caramel is already costed from Sugar*");
    }

    [Test]
    public void EnsureNoCycle_accepts_an_unrelated_component()
    {
        var componentIdsByIngredientId = new Dictionary<int, IReadOnlyList<int>>
        {
            [2] = [1],
        };

        var act = () => IngredientBomRules.EnsureNoCycle(ingredientId: 3, newComponentIds: [2], componentIdsByIngredientId, NameOf);

        act.Should().NotThrow();
    }

    [Test]
    public void CalculateCost_applies_weight_loss_and_weighted_average_cost()
    {
        // 1000g sugar @ ₱0.09/g (₱90) + 500g water @ ₱0/g, 20% weight loss.
        var (outputGrams, costPerGram) = IngredientBomRules.CalculateCost(
            [(Grams: 1000m, ComponentCostPerGram: 0.09m), (Grams: 500m, ComponentCostPerGram: 0m)],
            weightLossPercent: 20m);

        outputGrams.Should().Be(1200m);
        costPerGram.Should().Be(0.075m);
    }

    [Test]
    public void CalculateCost_with_zero_weight_loss_keeps_the_full_input_weight()
    {
        var (outputGrams, costPerGram) = IngredientBomRules.CalculateCost(
            [(Grams: 100m, ComponentCostPerGram: 1m)],
            weightLossPercent: 0m);

        outputGrams.Should().Be(100m);
        costPerGram.Should().Be(1m);
    }

    [Test]
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
