using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class RawMaterialRulesTests
{
    private static RawMaterial Purchased(decimal cost, decimal buffered, decimal bufferPercent, decimal? target) => new()
    {
        Source = RawMaterialSource.Purchased,
        CostPerBaseUnit = cost,
        BufferedCostPerBaseUnit = buffered,
        CostBufferPercent = bufferPercent,
        TargetCostBufferPercent = target,
    };

    public static IEnumerable<(decimal, decimal, bool)> BelowCostCases() =>
    [
        (0.06m, 0.05m, true), // -16.7%: prices have risen past it
        (0.05m, 0.05m, false), // 0%: no buffer, but not below
        (0.0518888m, 0.05188m, false), // -0.02%: a 0% buffer typed to the centavo in a 25 kg sack
        (0.05m, 0.055m, false),
    ];

    [Test]
    [MethodDataSource(nameof(BelowCostCases))]
    public void A_purchased_material_priced_past_its_buffered_cost_by_a_tenth_of_a_point_or_more_is_below_cost(
        decimal cost, decimal buffered, bool expected)
    {
        RawMaterialRules.IsBufferedBelowCost(Purchased(cost, buffered, 0m, null)).Should().Be(expected);
    }

    [Test]
    public void Only_a_priced_purchased_material_can_be_below_cost()
    {
        var made = new RawMaterial { Source = RawMaterialSource.Made, CostPerBaseUnit = 0.06m, BufferedCostPerBaseUnit = 0.05m };
        var unpriced = Purchased(0m, 0m, 10m, 10m);

        RawMaterialRules.IsBufferedBelowCost(made).Should().BeFalse("a made one's buffered cost is its components'");
        RawMaterialRules.IsBufferedBelowCost(unpriced).Should().BeFalse();
    }

    [Test]
    public void A_buffer_worn_below_its_target_by_a_tenth_of_a_point_or_more_is_counted()
    {
        // 0.055 over 0.05 is 10%; the price up to 0.052 leaves 5.77%, under a 10% target.
        Purchased(0.052m, 0.055m, 5.77m, 10m).Should().Match<RawMaterial>(m => RawMaterialRules.IsBufferBelowTarget(m));
        Purchased(0.05m, 0.055m, 10m, 10m).Should().Match<RawMaterial>(m => !RawMaterialRules.IsBufferBelowTarget(m));
        Purchased(0.1914m, 0.2105m, 9.98m, 10m).Should().Match<RawMaterial>(m => !RawMaterialRules.IsBufferBelowTarget(m), "9.98% rounds to the 10% target");
        RawMaterialRules.CurrentBufferPercentOf(Purchased(0.052m, 0.055m, 5.77m, 10m))!.Value.Should().BeApproximately(5.7692m, 0.0001m);
    }

    [Test]
    public void A_material_saved_before_targets_were_kept_takes_its_last_saved_buffer_as_its_target()
    {
        RawMaterialRules.TargetBufferPercentOf(Purchased(0.05m, 0.055m, 10m, null)).Should().Be(10m);
        RawMaterialRules.TargetBufferPercentOf(Purchased(0.05m, 0.055m, 10m, 12m)).Should().Be(12m);
        RawMaterialRules.CurrentBufferPercentOf(Purchased(0m, 0m, 10m, 10m)).Should().BeNull("there's no price to have a buffer over");
    }

    [Test]
    public void EnsureFieldsMatchType_accepts_an_ingredient_with_no_base_unit()
    {
        var ingredient = new RawMaterial { Type = RawMaterialType.Ingredient, Source = RawMaterialSource.Made, WeightLossPercent = 10m };

        var act = () => RawMaterialRules.EnsureFieldsMatchType(ingredient);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureFieldsMatchType_rejects_an_ingredient_with_a_base_unit_since_it_is_always_the_gram()
    {
        var ingredient = new RawMaterial { Type = RawMaterialType.Ingredient, BaseUnitId = 1 };

        var act = () => RawMaterialRules.EnsureFieldsMatchType(ingredient);

        act.Should().Throw<InvalidOperationException>().WithMessage("*gram*");
    }

    [Test]
    public void EnsureFieldsMatchType_accepts_a_purchased_packaging_material_with_a_base_unit()
    {
        var packaging = new RawMaterial { Type = RawMaterialType.Packaging, BaseUnitId = 1, Source = RawMaterialSource.Purchased };

        var act = () => RawMaterialRules.EnsureFieldsMatchType(packaging);

        act.Should().NotThrow();
    }

    [Test]
    [Arguments(null)]
    [Arguments(0)]
    public void EnsureFieldsMatchType_rejects_a_packaging_material_without_a_base_unit(int? baseUnitId)
    {
        var packaging = new RawMaterial { Type = RawMaterialType.Packaging, BaseUnitId = baseUnitId };

        var act = () => RawMaterialRules.EnsureFieldsMatchType(packaging);

        act.Should().Throw<InvalidOperationException>().WithMessage("*base unit*");
    }

    [Test]
    public void EnsureFieldsMatchType_rejects_a_made_packaging_material()
    {
        var packaging = new RawMaterial { Type = RawMaterialType.Packaging, BaseUnitId = 1, Source = RawMaterialSource.Made, WeightLossPercent = 0m };

        var act = () => RawMaterialRules.EnsureFieldsMatchType(packaging);

        act.Should().Throw<InvalidOperationException>().WithMessage("*packaging is always purchased*");
    }

    [Test]
    public void EnsureFieldsMatchSource_accepts_a_purchased_material_with_no_weight_loss_percent()
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Purchased };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(ingredient);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureFieldsMatchSource_accepts_a_made_ingredient_with_a_weight_loss_percent_and_a_sub_assembly()
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Made, WeightLossPercent = 20m, SubAssemblyId = 7 };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(ingredient);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureFieldsMatchSource_rejects_a_made_ingredient_without_a_sub_assembly()
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Made, WeightLossPercent = 20m };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<BusinessRuleException>().WithMessage("Pick the sub-assembly it's made from.");
    }

    [Test]
    [Arguments(RawMaterialSource.Purchased)]
    [Arguments(RawMaterialSource.Linked)]
    public void EnsureFieldsMatchSource_rejects_a_sub_assembly_on_an_ingredient_that_isnt_made(RawMaterialSource source)
    {
        var ingredient = new RawMaterial
        {
            Source = source,
            SubAssemblyId = 7,
            LinkedToId = source == RawMaterialSource.Linked ? 1 : null,
            WeightSharePercent = source == RawMaterialSource.Linked ? 50m : null,
            CostSharePercent = source == RawMaterialSource.Linked ? 50m : null,
        };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<BusinessRuleException>().WithMessage("Only a made ingredient is made from a sub-assembly.");
    }

    [Test]
    public void EnsureFieldsMatchSource_rejects_a_made_ingredient_without_a_weight_loss_percent()
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Made };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(100)]
    public void EnsureFieldsMatchSource_rejects_an_out_of_range_weight_loss_percent(decimal weightLossPercent)
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Made, WeightLossPercent = weightLossPercent };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureFieldsMatchSource_rejects_a_purchased_material_with_a_weight_loss_percent()
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Purchased, WeightLossPercent = 10m };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(ingredient);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureFieldsMatchSource_accepts_a_linked_ingredient_with_a_link_and_both_shares()
    {
        var eggWhite = new RawMaterial { Source = RawMaterialSource.Linked, LinkedToId = 1, WeightSharePercent = 70m, CostSharePercent = 30m };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(eggWhite);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureFieldsMatchSource_accepts_a_part_that_is_all_of_the_weight_and_cost()
    {
        var eggWhite = new RawMaterial { Source = RawMaterialSource.Linked, LinkedToId = 1, WeightSharePercent = 100m, CostSharePercent = 100m };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(eggWhite);

        act.Should().NotThrow();
    }

    [Test]
    [Arguments(null, 70, 30)]
    [Arguments(0, 70, 30)]
    public void EnsureFieldsMatchSource_rejects_a_linked_ingredient_without_a_link(int? linkedToId, int weightShare, int costShare)
    {
        var eggWhite = new RawMaterial { Source = RawMaterialSource.Linked, LinkedToId = linkedToId, WeightSharePercent = weightShare, CostSharePercent = costShare };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(eggWhite);

        act.Should().Throw<InvalidOperationException>().WithMessage("*linked to*");
    }

    public static IEnumerable<(decimal?, decimal?)> SharesMissingOrOutOfRange() =>
    [
        (null, 30m),
        (0m, 30m),
        (-5m, 30m),
        (100.01m, 30m),
        (70m, null),
        (70m, 0m),
        (70m, 101m),
    ];

    [Test]
    [MethodDataSource(nameof(SharesMissingOrOutOfRange))]
    public void EnsureFieldsMatchSource_rejects_a_missing_or_out_of_range_share(decimal? weightShare, decimal? costShare)
    {
        var eggWhite = new RawMaterial
        {
            Source = RawMaterialSource.Linked,
            LinkedToId = 1,
            WeightSharePercent = weightShare,
            CostSharePercent = costShare,
        };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(eggWhite);

        act.Should().Throw<InvalidOperationException>().WithMessage("*share must be above 0 and at most 100*");
    }

    [Test]
    public void EnsureFieldsMatchSource_rejects_a_linked_ingredient_with_a_weight_loss_percent()
    {
        var eggWhite = new RawMaterial { Source = RawMaterialSource.Linked, LinkedToId = 1, WeightSharePercent = 70m, CostSharePercent = 30m, WeightLossPercent = 0m };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(eggWhite);

        act.Should().Throw<InvalidOperationException>().WithMessage("*weight loss*");
    }

    [Test]
    [Arguments(RawMaterialSource.Purchased)]
    [Arguments(RawMaterialSource.Made)]
    public void EnsureFieldsMatchSource_rejects_link_fields_on_a_material_that_is_not_linked(RawMaterialSource source)
    {
        var material = new RawMaterial
        {
            Source = source,
            WeightLossPercent = source == RawMaterialSource.Made ? 0m : null,
            LinkedToId = 1,
        };

        var act = () => RawMaterialRules.EnsureFieldsMatchSource(material);

        act.Should().Throw<InvalidOperationException>().WithMessage("Only a linked ingredient*");
    }

    [Test]
    public void EnsureFieldsMatchType_rejects_a_linked_packaging_material()
    {
        var packaging = new RawMaterial { Type = RawMaterialType.Packaging, BaseUnitId = 1, Source = RawMaterialSource.Linked };

        var act = () => RawMaterialRules.EnsureFieldsMatchType(packaging);

        act.Should().Throw<InvalidOperationException>().WithMessage("*packaging is always purchased*");
    }

    [Test]
    public void LinkedCost_splits_an_eggs_cost_between_its_white_and_yolk_by_their_shares()
    {
        // ₱135 for 1,500 g is ₱0.09 a gram. White is 70% of the weight for 30% of the cost, yolk 30% for 70%.
        var eggCostPerGram = 135m / 1500m;

        var white = RawMaterialRules.LinkedCost(eggCostPerGram, weightSharePercent: 70m, costSharePercent: 30m);
        var yolk = RawMaterialRules.LinkedCost(eggCostPerGram, weightSharePercent: 30m, costSharePercent: 70m);

        white.Should().BeApproximately(0.0385714286m, 0.0000000001m);
        yolk.Should().Be(0.21m);

        // The whole egg's cost is accounted for: 1,050 g of white and 450 g of yolk.
        (1050m * white).Should().BeApproximately(40.50m, 0.0000001m);
        (450m * yolk).Should().Be(94.50m);
    }

    [Test]
    public void LinkedCost_keeps_the_cost_per_gram_when_the_shares_match()
    {
        RawMaterialRules.LinkedCost(0.09m, weightSharePercent: 40m, costSharePercent: 40m).Should().Be(0.09m);
    }

    [Test]
    public void EnsureValidLinkTarget_accepts_another_ingredient()
    {
        var act = () => RawMaterialRules.EnsureValidLinkTarget(id: 2, linkedToId: 1, RawMaterialType.Ingredient, "Egg");

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureValidLinkTarget_rejects_a_link_to_itself()
    {
        var act = () => RawMaterialRules.EnsureValidLinkTarget(id: 1, linkedToId: 1, RawMaterialType.Ingredient, "Egg");

        act.Should().Throw<InvalidOperationException>().WithMessage("*itself*");
    }

    [Test]
    public void EnsureValidLinkTarget_rejects_a_link_to_packaging()
    {
        var act = () => RawMaterialRules.EnsureValidLinkTarget(id: 0, linkedToId: 1, RawMaterialType.Packaging, "Egg Tray");

        act.Should().Throw<InvalidOperationException>().WithMessage("'Egg Tray' is packaging*");
    }

    [Test]
    public void EnsureLinkSharesFit_accepts_parts_that_add_up_to_exactly_all_of_it()
    {
        var act = () => RawMaterialRules.EnsureLinkSharesFit("Egg White", "Egg", 70m, 30m, [("Egg Yolk", 30m, 70m)]);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureLinkSharesFit_accepts_parts_that_leave_some_over()
    {
        // The shell is neither: 60% + 30% of the weight leaves 10%.
        var act = () => RawMaterialRules.EnsureLinkSharesFit("Egg White", "Egg", 60m, 30m, [("Egg Yolk", 30m, 70m)]);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureLinkSharesFit_rejects_more_than_all_of_the_weight_naming_the_other_parts()
    {
        var act = () => RawMaterialRules.EnsureLinkSharesFit("Egg White", "Egg", 75m, 30m, [("Egg Yolk", 30m, 70m)]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Egg Yolk (30%) already takes 30% of Egg's weight, so Egg White can take at most 70%.");
    }

    [Test]
    public void EnsureLinkSharesFit_rejects_more_than_all_of_the_cost_naming_the_other_parts()
    {
        var act = () => RawMaterialRules.EnsureLinkSharesFit("Egg Shell", "Egg", 10m, 5m, [("Egg White", 60m, 30m), ("Egg Yolk", 30m, 70m)]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Egg White (30%), Egg Yolk (70%) already take 100% of Egg's cost, so Egg Shell can take at most 0%.");
    }

    [Test]
    public void EnsureTypeUnchanged_accepts_the_same_type()
    {
        var act = () => RawMaterialRules.EnsureTypeUnchanged(RawMaterialType.Packaging, RawMaterialType.Packaging);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureTypeUnchanged_rejects_a_changed_type()
    {
        var act = () => RawMaterialRules.EnsureTypeUnchanged(RawMaterialType.Ingredient, RawMaterialType.Packaging);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureSourceUnchanged_accepts_the_same_source()
    {
        var act = () => RawMaterialRules.EnsureSourceUnchanged(RawMaterialSource.Purchased, RawMaterialSource.Purchased);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureSourceUnchanged_rejects_a_changed_source()
    {
        var act = () => RawMaterialRules.EnsureSourceUnchanged(RawMaterialSource.Purchased, RawMaterialSource.Made);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureUsableInRecipes_accepts_an_ingredient_and_names_packaging_it_rejects()
    {
        RawMaterialRules.EnsureUsableInRecipes(RawMaterialType.Ingredient, "Sugar");

        var act = () => RawMaterialRules.EnsureUsableInRecipes(RawMaterialType.Packaging, "Cake Box");

        act.Should().Throw<InvalidOperationException>().WithMessage("'Cake Box' is packaging*");
    }

    [Test]
    public void EnsureUsableAsPackaging_accepts_packaging_and_names_an_ingredient_it_rejects()
    {
        RawMaterialRules.EnsureUsableAsPackaging(RawMaterialType.Packaging, "Cake Box");

        var act = () => RawMaterialRules.EnsureUsableAsPackaging(RawMaterialType.Ingredient, "Sugar");

        act.Should().Throw<InvalidOperationException>().WithMessage("'Sugar' is an ingredient*");
    }

    [Test]
    public void ApplyBrandPrices_divides_the_price_by_the_base_units_in_its_unit()
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Purchased };

        RawMaterialRules.ApplyBrandPrices(ingredient, [(900m, 25000m)]); // ₱900 a 25 kg bag

        ingredient.CostPerBaseUnit.Should().Be(0.036m);
    }

    [Test]
    public void ApplyBrandPrices_treats_a_price_with_no_unit_as_per_base_unit()
    {
        var packaging = new RawMaterial { Type = RawMaterialType.Packaging, BaseUnitId = 1 };

        RawMaterialRules.ApplyBrandPrices(packaging, [(0.3m, null)]);

        packaging.CostPerBaseUnit.Should().Be(0.3m);
    }

    [Test]
    public void ApplyBrandPrices_costs_at_the_dearest_brand_per_base_unit_and_says_which()
    {
        var flour = new RawMaterial { Source = RawMaterialSource.Purchased };

        // ₱900 a 25 kg bag is ₱0.036/g; ₱40 a 1 kg pack is ₱0.04/g; ₱0.038 a gram sits between.
        var dearest = RawMaterialRules.ApplyBrandPrices(flour, [(900m, 25000m), (40m, 1000m), (0.038m, null)]);

        dearest.Should().Be(1);
        flour.CostPerBaseUnit.Should().Be(0.04m);
    }

    public static IEnumerable<decimal> NewPrices() => [0.08m, 0.02m];

    [Test]
    [MethodDataSource(nameof(NewPrices))]
    public void ApplyBrandPrices_leaves_a_buffered_cost_it_already_has_whatever_the_new_price(decimal price)
    {
        var flour = new RawMaterial { Source = RawMaterialSource.Purchased, CostBufferPercent = 5m, CostPerBaseUnit = 0.05m, BufferedCostPerBaseUnit = 0.0525m };

        RawMaterialRules.ApplyBrandPrices(flour, [(price, null)]);

        flour.CostPerBaseUnit.Should().Be(price);
        flour.BufferedCostPerBaseUnit.Should().Be(0.0525m, "a fixed amount, changed only by typing it or recalculating it");
    }

    [Test]
    public void EnsureValidBufferedCost_rejects_a_negative_amount_only()
    {
        RawMaterialRules.EnsureValidBufferedCost(0m);
        RawMaterialRules.EnsureValidBufferedCost(0.05m);

        var act = () => RawMaterialRules.EnsureValidBufferedCost(-0.01m);

        act.Should().Throw<InvalidOperationException>().WithMessage("Buffered cost can't be negative.");
    }

    public static IEnumerable<(CostBasis, decimal)> CostsAtEachBasis() =>
    [
        (CostBasis.Buffered, 0.0525m),
        (CostBasis.Unbuffered, 0.05m),
    ];

    [Test]
    [MethodDataSource(nameof(CostsAtEachBasis))]
    public void CostPerBaseUnitAt_reads_the_buffered_or_the_true_cost(CostBasis basis, decimal expected)
    {
        var flour = new RawMaterial { CostPerBaseUnit = 0.05m, BufferedCostPerBaseUnit = 0.0525m };

        RawMaterialRules.CostPerBaseUnitAt(flour, basis).Should().Be(expected);
    }

    [Test]
    public void FindDearestBrandPrice_takes_the_first_on_a_tie()
    {
        RawMaterialRules.FindDearestBrandPrice([(40m, 1000m), (0.04m, null)]).Should().Be(0);
    }

    [Test]
    public void A_preferred_brand_is_the_cost_its_dearest_unit_when_priced_in_more_than_one()
    {
        // Gold at ₱930 a 25 kg sack (0.0372/g) and ₱40 a kilo (0.04/g); Kingbake at ₱1,250 a 25 kg sack (0.05/g), the dearest.
        (string?, decimal, decimal?)[] prices = [("Gold", 930m, 25000m), ("Kingbake", 1250m, 25000m), (" gold ", 40m, 1000m)];

        RawMaterialRules.FindCostBrandPrice(prices, null).Should().Be(1, "no brand picked: the dearest");
        RawMaterialRules.FindCostBrandPrice(prices, "GOLD").Should().Be(2, "Gold's dearest unit, the brand matched as brands are");
        RawMaterialRules.FindCostBrandPrice(prices, "Bakers Best").Should().Be(1, "a brand with no price falls back to the dearest");
        RawMaterialRules.IsBrandPriced(prices.Select(p => p.Item1), "gold").Should().BeTrue();
        RawMaterialRules.IsBrandPriced(prices.Select(p => p.Item1), "Bakers Best").Should().BeFalse();
    }

    [Test]
    public void ApplyBrandPrices_costs_a_material_at_its_preferred_brand()
    {
        var flour = new RawMaterial { Source = RawMaterialSource.Purchased, PreferredBrand = "Gold", CostBufferPercent = 10m };

        var source = RawMaterialRules.ApplyBrandPrices(flour, [("Kingbake", 1250m, 25000m), ("Gold", 1000m, 25000m)]);

        source.Should().Be(1);
        flour.CostPerBaseUnit.Should().Be(0.04m);
        flour.BufferedCostPerBaseUnit.Should().Be(0.044m, "a first price puts the buffer on top of the cost it gives");
    }

    [Test]
    public void ApplyBrandPrices_with_no_prices_zeroes_the_cost()
    {
        var ingredient = new RawMaterial { Source = RawMaterialSource.Purchased, CostPerBaseUnit = 0.036m, BufferedCostPerBaseUnit = 0.036m };

        var dearest = RawMaterialRules.ApplyBrandPrices(ingredient, Array.Empty<(decimal, decimal?)>());

        dearest.Should().BeNull();
        ingredient.CostPerBaseUnit.Should().Be(0m);
        ingredient.BufferedCostPerBaseUnit.Should().Be(0m);
    }

    [Test]
    public void ApplyBrandPrices_puts_the_materials_buffer_on_top_of_the_buffered_cost()
    {
        var flour = new RawMaterial { Source = RawMaterialSource.Purchased, CostBufferPercent = 5m };

        RawMaterialRules.ApplyBrandPrices(flour, [(1250m, 25000m)]); // ₱1,250 a 25 kg bag

        flour.CostPerBaseUnit.Should().Be(0.05m);
        flour.BufferedCostPerBaseUnit.Should().Be(0.0525m);
    }

    [Test]
    public void ApplyBrandPrices_with_no_buffer_makes_the_buffered_cost_the_cost()
    {
        var sugar = new RawMaterial { Source = RawMaterialSource.Purchased };

        RawMaterialRules.ApplyBrandPrices(sugar, [(900m, 25000m)]);

        sugar.BufferedCostPerBaseUnit.Should().Be(sugar.CostPerBaseUnit);
    }

    [Test]
    public void EnsureDistinctBrandPrices_accepts_one_brand_in_two_units()
    {
        var act = () => RawMaterialRules.EnsureDistinctBrandPrices([("Gold Medal", 1), ("Gold Medal", 2), (null, 1)]);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureDistinctBrandPrices_rejects_a_brand_twice_in_one_unit_whatever_its_case_or_spacing()
    {
        var act = () => RawMaterialRules.EnsureDistinctBrandPrices([("Gold Medal", 1), (" gold medal ", 1)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("'Gold Medal' is priced twice*");
    }

    [Test]
    public void EnsureDistinctBrandPrices_rejects_two_unnamed_brands_in_one_unit()
    {
        var act = () => RawMaterialRules.EnsureDistinctBrandPrices([(null, null), ("  ", null)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("Two prices without a brand*");
    }

    public static IEnumerable<(decimal, decimal, decimal)> BufferedCosts() =>
    [
        (10m, 0m, 10m),
        (10m, 5m, 10.5m),
        (0.05m, 5m, 0.0525m),
        (10m, 100m, 20m),
    ];

    [Test]
    [MethodDataSource(nameof(BufferedCosts))]
    public void BufferedCost_adds_the_buffer_percent_to_the_cost(decimal cost, decimal bufferPercent, decimal expected)
    {
        RawMaterialRules.BufferedCost(cost, bufferPercent).Should().Be(expected);
    }

    [Test]
    public void StoredCostBufferPercent_is_always_0_for_a_made_ingredient()
    {
        // Its components' buffers are already in its BOM rollup, and it has no buffer box on the screen, so a
        // percent sent for it (a stale one loaded from the row) is normalised away rather than rejected.
        RawMaterialRules.StoredCostBufferPercent(RawMaterialSource.Made, inheritedBuffer: null, sent: 84.4147m).Should().Be(0m);
    }

    [Test]
    public void StoredCostBufferPercent_takes_the_linked_to_ingredients_for_a_linked_one_and_what_was_sent_otherwise()
    {
        RawMaterialRules.StoredCostBufferPercent(RawMaterialSource.Linked, inheritedBuffer: 10m, sent: 5m).Should().Be(10m);
        RawMaterialRules.StoredCostBufferPercent(RawMaterialSource.Purchased, inheritedBuffer: null, sent: 5m).Should().Be(5m);
    }

    public static IEnumerable<(decimal, decimal, decimal)> BufferPercents() =>
    [
        (930m, 976.5m, 5m),
        (930m, 930m, 0m),
        (10m, 20m, 100m),
    ];

    [Test]
    [MethodDataSource(nameof(BufferPercents))]
    public void CostBufferPercentOf_works_out_the_buffer_from_the_buffered_cost(decimal cost, decimal bufferedCost, decimal expected)
    {
        RawMaterialRules.CostBufferPercentOf(cost, bufferedCost).Should().Be(expected);
    }

    [Test]
    public void CostBufferPercentOf_undoes_BufferedCost()
    {
        var buffered = RawMaterialRules.BufferedCost(1250m, 7.5m);

        RawMaterialRules.CostBufferPercentOf(1250m, buffered).Should().Be(7.5m);
    }

    [Test]
    [Arguments(0)]
    [Arguments(5)]
    [Arguments(100)]
    public void EnsureValidCostBuffer_accepts_0_to_100_percent(decimal bufferPercent)
    {
        var act = () => RawMaterialRules.EnsureValidCostBuffer(bufferPercent);

        act.Should().NotThrow();
    }

    public static IEnumerable<decimal> BuffersOutOfRange() => [-1m, 100.01m];

    [Test]
    [MethodDataSource(nameof(BuffersOutOfRange))]
    public void EnsureValidCostBuffer_rejects_a_buffer_outside_0_to_100_percent(decimal bufferPercent)
    {
        var act = () => RawMaterialRules.EnsureValidCostBuffer(bufferPercent);

        act.Should().Throw<InvalidOperationException>().WithMessage("Cost buffer*");
    }

    [Test]
    public void EnsureNotTheBaseUnit_accepts_another_unit()
    {
        var act = () => RawMaterialRules.EnsureNotTheBaseUnit(baseUnitId: 1, unitOfMeasureId: 2);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureNotTheBaseUnit_rejects_the_base_unit_itself()
    {
        var act = () => RawMaterialRules.EnsureNotTheBaseUnit(baseUnitId: 1, unitOfMeasureId: 1);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureNotTheBaseUnit_accepts_any_unit_for_an_ingredient_whose_gram_is_no_unit_of_measure_row()
    {
        var act = () => RawMaterialRules.EnsureNotTheBaseUnit(baseUnitId: null, unitOfMeasureId: 1);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureUnitNotAlreadyAdded_accepts_a_unit_the_material_does_not_have_yet()
    {
        var act = () => RawMaterialRules.EnsureUnitNotAlreadyAdded(alreadyAdded: false);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureUnitNotAlreadyAdded_rejects_a_second_unit_in_the_same_unit_of_measure()
    {
        var act = () => RawMaterialRules.EnsureUnitNotAlreadyAdded(alreadyAdded: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*already has that unit*");
    }

    [Test]
    public void EnsureBaseUnitCanChange_accepts_a_material_with_nothing_measured_in_it_yet()
    {
        var act = () => RawMaterialRules.EnsureBaseUnitCanChange(hasUnits: false, hasPrices: false, hasLines: false);

        act.Should().NotThrow();
    }

    [Test]
    [Arguments(true, false, false)]
    [Arguments(false, true, false)]
    [Arguments(false, false, true)]
    public void EnsureBaseUnitCanChange_rejects_a_material_with_units_prices_or_lines(bool hasUnits, bool hasPrices, bool hasLines)
    {
        var act = () => RawMaterialRules.EnsureBaseUnitCanChange(hasUnits, hasPrices, hasLines);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void StoredCost_rounds_to_the_20_places_a_cost_is_stored_to_as_SQL_Server_does()
    {
        // ₱170 a pack of 240, and ₱0.47 an egg of 3 g: one rounds down, one up, as the database stores them.
        RawMaterialRules.StoredCost(170m / 240m).Should().Be(0.70833333333333333333m);
        RawMaterialRules.StoredCost(0.47m / 3m).Should().Be(0.15666666666666666667m);
        RawMaterialRules.StoredCost(0.05m).Should().Be(0.05m, "a cost that comes out even is left as it is");
    }

    [Test]
    public void StoredCost_rounds_a_half_away_from_zero()
    {
        RawMaterialRules.StoredCost(0.000000000000000000005m).Should().Be(0.00000000000000000001m);
    }

    [Test]
    public void ApplyBrandPrices_sets_both_costs_as_they_are_stored()
    {
        var material = new RawMaterial { Name = "Cello Sheet", CostBufferPercent = 10m };

        RawMaterialRules.ApplyBrandPrices(material, [(170m, 240m)]);

        material.CostPerBaseUnit.Should().Be(0.70833333333333333333m, "compared with the stored cost, it mustn't read as a change");
        material.BufferedCostPerBaseUnit.Should().Be(0.77916666666666666667m, "the cost with its 10% buffer on top, stored the same way");
    }

    [Test]
    [Arguments(RawMaterialType.Ingredient, RawMaterialSource.Purchased, RawMaterialCategoryType.Ingredient)]
    [Arguments(RawMaterialType.Ingredient, RawMaterialSource.Linked, RawMaterialCategoryType.Ingredient)]
    [Arguments(RawMaterialType.Ingredient, RawMaterialSource.Made, RawMaterialCategoryType.Made)]
    [Arguments(RawMaterialType.Packaging, RawMaterialSource.Purchased, RawMaterialCategoryType.Packaging)]
    public void CategoryTypeFor_puts_a_made_ingredient_in_a_Made_category_and_every_other_material_in_one_of_its_type(
        RawMaterialType type, RawMaterialSource source, RawMaterialCategoryType expected)
    {
        RawMaterialRules.CategoryTypeFor(type, source).Should().Be(expected);
    }

    [Test]
    [Arguments(RawMaterialCategoryType.Ingredient, RawMaterialType.Ingredient)]
    [Arguments(RawMaterialCategoryType.Made, RawMaterialType.Ingredient)]
    [Arguments(RawMaterialCategoryType.Packaging, RawMaterialType.Packaging)]
    public void MaterialTypeOf_counts_a_Made_category_among_the_ingredient_ones(RawMaterialCategoryType categoryType, RawMaterialType expected)
    {
        RawMaterialRules.MaterialTypeOf(categoryType).Should().Be(expected);
    }

    [Test]
    [Arguments(RawMaterialType.Ingredient, RawMaterialSource.Purchased, RawMaterialCategoryType.Made,
        "In-House is a Made category, for made ingredients only. Put this ingredient in a category for ingredients.")]
    [Arguments(RawMaterialType.Ingredient, RawMaterialSource.Linked, RawMaterialCategoryType.Made,
        "In-House is a Made category, for made ingredients only. Put this ingredient in a category for ingredients.")]
    [Arguments(RawMaterialType.Ingredient, RawMaterialSource.Made, RawMaterialCategoryType.Ingredient,
        "In-House is an ingredient category. Put this made ingredient in a Made category.")]
    [Arguments(RawMaterialType.Ingredient, RawMaterialSource.Made, RawMaterialCategoryType.Packaging,
        "In-House is a packaging category. Put this made ingredient in a Made category.")]
    [Arguments(RawMaterialType.Packaging, RawMaterialSource.Purchased, RawMaterialCategoryType.Made,
        "In-House is a Made category, for made ingredients only. Put this packaging material in a category for packaging.")]
    public void EnsureCategoryMatchesType_rejects_a_category_of_another_type_saying_where_the_material_goes(
        RawMaterialType type, RawMaterialSource source, RawMaterialCategoryType categoryType, string message)
    {
        var act = () => RawMaterialRules.EnsureCategoryMatchesType(type, source, categoryType, "In-House");

        act.Should().Throw<BusinessRuleException>().WithMessage(message);
    }

    [Test]
    [Arguments(RawMaterialType.Ingredient, RawMaterialSource.Purchased)]
    [Arguments(RawMaterialType.Ingredient, RawMaterialSource.Linked)]
    [Arguments(RawMaterialType.Ingredient, RawMaterialSource.Made)]
    [Arguments(RawMaterialType.Packaging, RawMaterialSource.Purchased)]
    public void EnsureCategoryMatchesType_accepts_a_category_of_the_materials_own_type(RawMaterialType type, RawMaterialSource source)
    {
        var act = () => RawMaterialRules.EnsureCategoryMatchesType(type, source, RawMaterialRules.CategoryTypeFor(type, source), "Flour");

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureCategoryTypeCanChange_accepts_a_change_everything_in_the_category_fits()
    {
        var act = () => RawMaterialRules.EnsureCategoryTypeCanChange("In-House", RawMaterialCategoryType.Made, materialsThatDontFit: 0);

        act.Should().NotThrow();
    }

    [Test]
    [Arguments(1, RawMaterialCategoryType.Made, "Filling has 1 material that can't go in a Made category, so its type can't change. Move it to another category first.")]
    [Arguments(7, RawMaterialCategoryType.Packaging, "Filling has 7 materials that can't go in a packaging category, so its type can't change. Move them to another category first.")]
    public void EnsureCategoryTypeCanChange_rejects_a_change_that_leaves_materials_in_a_category_they_cant_go_in(
        int materialsThatDontFit, RawMaterialCategoryType newType, string message)
    {
        var act = () => RawMaterialRules.EnsureCategoryTypeCanChange("Filling", newType, materialsThatDontFit);

        act.Should().Throw<BusinessRuleException>().WithMessage(message);
    }

    [Test]
    [Arguments("Flour", "Flour")]
    [Arguments("Flour ", "Flour")]
    [Arguments("  Bread Flour  ", "Bread Flour")]
    public void A_name_is_stored_without_spaces_at_either_end(string name, string expected)
    {
        RawMaterialRules.StoredName(name).Should().Be(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public void A_name_with_nothing_but_spaces_is_refused(string name)
    {
        var act = () => RawMaterialRules.StoredName(name);

        act.Should().Throw<BusinessRuleException>().WithMessage("Every raw material needs a name.");
    }

    [Test]
    public void BusinessRuleException_is_still_an_InvalidOperationException()
    {
        var act = () => RawMaterialRules.EnsureValidBrandPrice(0m);

        act.Should().Throw<BusinessRuleException>().Which.Should().BeAssignableTo<InvalidOperationException>();
    }

    [Test]
    public void A_material_nothing_uses_can_be_deleted()
    {
        var act = () => RawMaterialRules.EnsureNotInUse("Salt", []);

        act.Should().NotThrow();
    }

    [Test]
    public void A_material_in_use_names_each_user_once_in_order_with_how_it_uses_it()
    {
        var act = () => RawMaterialRules.EnsureNotInUse(
            "Butter", [("Ube Bun", "add-in"), ("Butter Icing Base", "recipe"), ("Ube Bun", "add-in"), ("ensaymada", "packaging")]);

        act.Should().Throw<BusinessRuleException>().Which.Message.Should().Be(
            "Butter can't be deleted while it's used: Butter Icing Base (recipe), ensaymada (packaging), Ube Bun (add-in). Take it out of them first.");
    }
}
