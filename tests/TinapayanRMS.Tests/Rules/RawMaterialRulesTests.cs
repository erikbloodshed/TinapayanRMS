using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Tests.Rules;

public class RawMaterialRulesTests
{
    [Fact]
    public void EnsureFieldsMatchType_accepts_an_ingredient_with_no_base_unit()
    {
        var ingredient = new RawMaterial { Type = RawMaterialType.Ingredient, Source = RawMaterialSource.Made, WeightLossPercent = 10m };

        var act = () => RawMaterialRules.EnsureFieldsMatchType(ingredient);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureFieldsMatchType_rejects_an_ingredient_with_a_base_unit_since_it_is_always_the_gram()
    {
        var ingredient = new RawMaterial { Type = RawMaterialType.Ingredient, BaseUnitId = 1 };

        var act = () => RawMaterialRules.EnsureFieldsMatchType(ingredient);

        act.Should().Throw<InvalidOperationException>().WithMessage("*gram*");
    }

    [Fact]
    public void EnsureFieldsMatchType_accepts_a_purchased_packaging_material_with_a_base_unit()
    {
        var packaging = new RawMaterial { Type = RawMaterialType.Packaging, BaseUnitId = 1, Source = RawMaterialSource.Purchased };

        var act = () => RawMaterialRules.EnsureFieldsMatchType(packaging);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void EnsureFieldsMatchType_rejects_a_packaging_material_without_a_base_unit(int? baseUnitId)
    {
        var packaging = new RawMaterial { Type = RawMaterialType.Packaging, BaseUnitId = baseUnitId };

        var act = () => RawMaterialRules.EnsureFieldsMatchType(packaging);

        act.Should().Throw<InvalidOperationException>().WithMessage("*base unit*");
    }

    [Fact]
    public void EnsureFieldsMatchType_rejects_a_made_packaging_material()
    {
        var packaging = new RawMaterial { Type = RawMaterialType.Packaging, BaseUnitId = 1, Source = RawMaterialSource.Made, WeightLossPercent = 0m };

        var act = () => RawMaterialRules.EnsureFieldsMatchType(packaging);

        act.Should().Throw<InvalidOperationException>().WithMessage("*packaging is always purchased*");
    }

    [Fact]
    public void EnsureFieldsMatchSource_accepts_a_purchased_material_with_no_weight_loss_percent()
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Purchased };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(ingredient);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureFieldsMatchSource_accepts_a_made_ingredient_with_a_weight_loss_percent()
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Made, WeightLossPercent = 20m };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(ingredient);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureFieldsMatchSource_rejects_a_made_ingredient_without_a_weight_loss_percent()
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Made };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void EnsureFieldsMatchSource_rejects_an_out_of_range_weight_loss_percent(decimal weightLossPercent)
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Made, WeightLossPercent = weightLossPercent };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureFieldsMatchSource_rejects_a_purchased_material_with_a_weight_loss_percent()
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Purchased, WeightLossPercent = 10m };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureTypeUnchanged_accepts_the_same_type()
    {
        var act = () => RawMaterialRules.EnsureTypeUnchanged(RawMaterialType.Packaging, RawMaterialType.Packaging);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureTypeUnchanged_rejects_a_changed_type()
    {
        var act = () => RawMaterialRules.EnsureTypeUnchanged(RawMaterialType.Ingredient, RawMaterialType.Packaging);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureSourceUnchanged_accepts_the_same_source()
    {
        var act = () => RawMaterialRules.EnsureSourceUnchanged(RawMaterialSource.Purchased, RawMaterialSource.Purchased);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureSourceUnchanged_rejects_a_changed_source()
    {
        var act = () => RawMaterialRules.EnsureSourceUnchanged(RawMaterialSource.Purchased, RawMaterialSource.Made);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureUsableInRecipes_accepts_an_ingredient_and_names_packaging_it_rejects()
    {
        RawMaterialRules.EnsureUsableInRecipes(RawMaterialType.Ingredient, "Sugar");

        var act = () => RawMaterialRules.EnsureUsableInRecipes(RawMaterialType.Packaging, "Cake Box");

        act.Should().Throw<InvalidOperationException>().WithMessage("'Cake Box' is packaging*");
    }

    [Fact]
    public void EnsureUsableAsPackaging_accepts_packaging_and_names_an_ingredient_it_rejects()
    {
        RawMaterialRules.EnsureUsableAsPackaging(RawMaterialType.Packaging, "Cake Box");

        var act = () => RawMaterialRules.EnsureUsableAsPackaging(RawMaterialType.Ingredient, "Sugar");

        act.Should().Throw<InvalidOperationException>().WithMessage("'Sugar' is an ingredient*");
    }

    [Fact]
    public void ApplyLatestPrice_divides_the_price_by_the_base_units_in_its_unit()
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Purchased };

        RawMaterialRules.ApplyLatestPrice(ingredient, latestPrice: 900m, baseUnitsPerUnit: 25000m); // ₱900 a 25 kg bag

        ingredient.CostPerBaseUnit.Should().Be(0.036m);
    }

    [Fact]
    public void ApplyLatestPrice_treats_a_price_with_no_unit_as_per_base_unit()
    {
        var packaging = new RawMaterial { Type = RawMaterialType.Packaging, BaseUnitId = 1 };

        RawMaterialRules.ApplyLatestPrice(packaging, latestPrice: 0.3m, baseUnitsPerUnit: null);

        packaging.CostPerBaseUnit.Should().Be(0.3m);
    }

    [Fact]
    public void ApplyLatestPrice_with_no_price_zeroes_the_cost()
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Purchased, CostPerBaseUnit = 0.036m };

        RawMaterialRules.ApplyLatestPrice(ingredient, latestPrice: null, baseUnitsPerUnit: null);

        ingredient.CostPerBaseUnit.Should().Be(0m);
    }

    [Fact]
    public void EnsureNotTheBaseUnit_accepts_another_unit()
    {
        var act = () => RawMaterialRules.EnsureNotTheBaseUnit(baseUnitId: 1, unitOfMeasureId: 2);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureNotTheBaseUnit_rejects_the_base_unit_itself()
    {
        var act = () => RawMaterialRules.EnsureNotTheBaseUnit(baseUnitId: 1, unitOfMeasureId: 1);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureNotTheBaseUnit_accepts_any_unit_for_an_ingredient_whose_gram_is_no_unit_of_measure_row()
    {
        var act = () => RawMaterialRules.EnsureNotTheBaseUnit(baseUnitId: null, unitOfMeasureId: 1);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureUnitNotAlreadyAdded_accepts_a_unit_the_material_does_not_have_yet()
    {
        var act = () => RawMaterialRules.EnsureUnitNotAlreadyAdded(alreadyAdded: false);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureUnitNotAlreadyAdded_rejects_a_second_unit_in_the_same_unit_of_measure()
    {
        var act = () => RawMaterialRules.EnsureUnitNotAlreadyAdded(alreadyAdded: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*already has that unit*");
    }

    [Fact]
    public void EnsureBaseUnitCanChange_accepts_a_material_with_nothing_measured_in_it_yet()
    {
        var act = () => RawMaterialRules.EnsureBaseUnitCanChange(hasUnits: false, hasPrices: false, hasLines: false);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void EnsureBaseUnitCanChange_rejects_a_material_with_units_prices_or_lines(bool hasUnits, bool hasPrices, bool hasLines)
    {
        var act = () => RawMaterialRules.EnsureBaseUnitCanChange(hasUnits, hasPrices, hasLines);

        act.Should().Throw<InvalidOperationException>();
    }
}
