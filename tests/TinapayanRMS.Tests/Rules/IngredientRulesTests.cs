using AwesomeAssertions;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Tests;

public class IngredientRulesTests
{
    [Fact]
    public void EnsureFieldsMatchSource_accepts_a_purchased_ingredient_with_unit_and_quantity()
    {
        var ingredient = new Ingredient
        {
            Source = IngredientSource.Purchased,
            PurchaseUnitId = 1,
            PurchaseUnitQty = 25000m,
        };

        var act = () => IngredientRules.EnsureFieldsMatchSource(ingredient);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureFieldsMatchSource_rejects_a_purchased_ingredient_without_a_purchase_unit()
    {
        var ingredient = new Ingredient
        {
            Source = IngredientSource.Purchased,
            PurchaseUnitId = null,
            PurchaseUnitQty = 25000m,
        };

        var act = () => IngredientRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureFieldsMatchSource_rejects_a_purchased_ingredient_with_a_non_positive_quantity()
    {
        var ingredient = new Ingredient
        {
            Source = IngredientSource.Purchased,
            PurchaseUnitId = 1,
            PurchaseUnitQty = 0m,
        };

        var act = () => IngredientRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureFieldsMatchSource_accepts_a_made_ingredient_with_no_purchase_fields()
    {
        var ingredient = new Ingredient { Source = IngredientSource.Made, WeightLossPercent = 20m };

        var act = () => IngredientRules.EnsureFieldsMatchSource(ingredient);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureFieldsMatchSource_rejects_a_made_ingredient_without_a_weight_loss_percent()
    {
        var ingredient = new Ingredient { Source = IngredientSource.Made };

        var act = () => IngredientRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void EnsureFieldsMatchSource_rejects_an_out_of_range_weight_loss_percent(decimal weightLossPercent)
    {
        var ingredient = new Ingredient { Source = IngredientSource.Made, WeightLossPercent = weightLossPercent };

        var act = () => IngredientRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureFieldsMatchSource_rejects_a_purchased_ingredient_with_a_weight_loss_percent()
    {
        var ingredient = new Ingredient
        {
            Source = IngredientSource.Purchased,
            PurchaseUnitId = 1,
            PurchaseUnitQty = 1000m,
            WeightLossPercent = 10m,
        };

        var act = () => IngredientRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureFieldsMatchSource_rejects_a_made_ingredient_with_a_purchase_unit()
    {
        var ingredient = new Ingredient { Source = IngredientSource.Made, PurchaseUnitId = 1 };

        var act = () => IngredientRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureFieldsMatchSource_rejects_a_made_ingredient_with_a_current_cost()
    {
        var ingredient = new Ingredient { Source = IngredientSource.Made, WeightLossPercent = 10m, CurrentCost = 5m };

        var act = () => IngredientRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureSourceUnchanged_accepts_the_same_source()
    {
        var act = () => IngredientRules.EnsureSourceUnchanged(IngredientSource.Purchased, IngredientSource.Purchased);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureSourceUnchanged_rejects_a_changed_source()
    {
        var act = () => IngredientRules.EnsureSourceUnchanged(IngredientSource.Purchased, IngredientSource.Made);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ApplyLatestPrice_sets_CurrentCost_and_CostPerGram_from_the_latest_entry()
    {
        var ingredient = new Ingredient { Source = IngredientSource.Purchased, PurchaseUnitQty = 25000m };
        var latest = new IngredientPriceHistory { Price = 900m };

        IngredientRules.ApplyLatestPrice(ingredient, latest);

        ingredient.CurrentCost.Should().Be(900m);
        ingredient.CostPerGram.Should().Be(0.036m);
    }

    [Fact]
    public void ApplyLatestPrice_with_no_entries_clears_CurrentCost_and_zeroes_CostPerGram()
    {
        var ingredient = new Ingredient
        {
            Source = IngredientSource.Purchased,
            PurchaseUnitQty = 25000m,
            CurrentCost = 900m,
            CostPerGram = 0.036m,
        };

        IngredientRules.ApplyLatestPrice(ingredient, null);

        ingredient.CurrentCost.Should().BeNull();
        ingredient.CostPerGram.Should().Be(0m);
    }

    [Fact]
    public void RecomputeCostPerGramFromCurrentCost_divides_CurrentCost_by_the_new_PurchaseUnitQty()
    {
        var ingredient = new Ingredient
        {
            Source = IngredientSource.Purchased,
            CurrentCost = 900m,
            PurchaseUnitQty = 30000m, // corrected from 25000m -- no new price entry
        };

        IngredientRules.RecomputeCostPerGramFromCurrentCost(ingredient);

        ingredient.CostPerGram.Should().Be(0.03m);
    }

    [Fact]
    public void RecomputeCostPerGramFromCurrentCost_zeroes_CostPerGram_when_unpriced()
    {
        var ingredient = new Ingredient { Source = IngredientSource.Purchased, CurrentCost = null, PurchaseUnitQty = 25000m };

        IngredientRules.RecomputeCostPerGramFromCurrentCost(ingredient);

        ingredient.CostPerGram.Should().Be(0m);
    }
}
