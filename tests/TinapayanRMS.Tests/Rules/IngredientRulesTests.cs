using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Tests.Rules;

public class IngredientRulesTests
{
    [Fact]
    public void EnsureFieldsMatchSource_accepts_a_purchased_ingredient_with_no_weight_loss_percent()
    {
        var ingredient = new Ingredient { Source = IngredientSource.Purchased };

        var act = () => IngredientRules.EnsureFieldsMatchSource(ingredient);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureFieldsMatchSource_accepts_a_made_ingredient_with_a_weight_loss_percent()
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
        var ingredient = new Ingredient { Source = IngredientSource.Purchased, WeightLossPercent = 10m };

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
    public void ApplyLatestPrice_divides_the_price_by_the_grams_in_its_unit()
    {
        var ingredient = new Ingredient { Source = IngredientSource.Purchased };

        IngredientRules.ApplyLatestPrice(ingredient, latestPrice: 900m, gramsPerUnit: 25000m); // ₱900 a 25 kg bag

        ingredient.CostPerGram.Should().Be(0.036m);
    }

    [Fact]
    public void ApplyLatestPrice_treats_a_price_with_no_unit_as_per_gram()
    {
        var ingredient = new Ingredient { Source = IngredientSource.Purchased };

        IngredientRules.ApplyLatestPrice(ingredient, latestPrice: 0.05m, gramsPerUnit: null);

        ingredient.CostPerGram.Should().Be(0.05m);
    }

    [Fact]
    public void ApplyLatestPrice_with_no_price_zeroes_CostPerGram()
    {
        var ingredient = new Ingredient { Source = IngredientSource.Purchased, CostPerGram = 0.036m };

        IngredientRules.ApplyLatestPrice(ingredient, latestPrice: null, gramsPerUnit: null);

        ingredient.CostPerGram.Should().Be(0m);
    }
}
