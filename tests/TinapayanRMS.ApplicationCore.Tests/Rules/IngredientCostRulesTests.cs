using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class IngredientCostRulesTests
{
    [Test]
    public void A_made_ingredient_rolls_up_its_lines_true_and_buffered_after_its_weight_loss()
    {
        var flour = Purchased(1, 0.05m, 0.055m);
        var sugar = Purchased(2, 0.08m, 0.088m);
        var syrup = Made(3, weightLossPercent: 20m);
        var materials = Index(flour, sugar, syrup);

        var costs = IngredientCostRules.Recompute(syrup, [Line(3, 1, 1000m), Line(3, 2, 500m)], id => materials[id]);

        // 50 + 40 = 90 of true cost, 55 + 44 = 99 buffered, over the 1,200 g left after 20% is lost.
        costs.Should().Be(new IngredientCosts(0.075m, 0.0825m, 0m));
    }

    [Test]
    public void A_linked_ingredient_takes_its_share_of_both_costs_and_the_buffer_percent()
    {
        var egg = Purchased(1, 0.014m, 0.0154m, bufferPercent: 10m);
        var white = Linked(2, linkedToId: 1, weightShare: 70m, costShare: 30m);

        var costs = IngredientCostRules.Recompute(white, [], _ => egg);

        costs.CostPerBaseUnit.Should().Be(RawMaterialRules.StoredCost(0.014m * 30m / 70m));
        costs.BufferedCostPerBaseUnit.Should().Be(RawMaterialRules.StoredCost(0.0154m * 30m / 70m));
        costs.CostBufferPercent.Should().Be(10m);
    }

    [Test]
    public void A_link_missing_its_shares_is_worked_out_as_a_made_one_with_no_lines()
    {
        var broken = new RawMaterial { Id = 2, Source = RawMaterialSource.Linked, LinkedToId = 1 };

        IngredientCostRules.Recompute(broken, [], _ => throw new InvalidOperationException("Not read")).Should().Be(new IngredientCosts(0m, 0m, 0m));
    }

    [Test]
    public void Dependencies_are_bom_components_and_links_together()
    {
        var dependencies = IngredientCostRules.Dependencies(
            [Purchased(1, 0m, 0m), Linked(2, 1, 70m, 30m), Made(3)],
            [Line(3, 2, 100m), Line(3, 4, 50m)]);

        dependencies.Should().HaveCount(2);
        dependencies[2].Should().Equal(1);
        dependencies[3].Should().Equal(2, 4);
    }

    [Test]
    public void Reprice_reaches_everything_that_depends_on_a_change_in_order_and_touches_nothing_given()
    {
        // Egg → egg white (linked) → custard (made from white and sugar); syrup is made from sugar alone.
        var egg = Purchased(1, 0.014m, 0.0154m);
        var white = Linked(2, linkedToId: 1, weightShare: 70m, costShare: 30m);
        var sugar = Purchased(3, 0.08m, 0.088m);
        var custard = Made(4);
        var syrup = Made(5);
        var materials = Index(egg, white, sugar, custard, syrup);
        IngredientBomLine[] lines = [Line(4, 2, 700m), Line(4, 3, 300m), Line(5, 3, 1000m)];
        foreach (var id in new[] { 2, 4, 5 })
        {
            (materials[id].CostPerBaseUnit, materials[id].BufferedCostPerBaseUnit, _) =
                IngredientCostRules.Recompute(materials[id], lines.Where(l => l.IngredientId == id), other => materials[other]);
        }

        var custardBefore = custard.CostPerBaseUnit;

        var copies = IngredientCostRules.Reprice(materials, lines, new Dictionary<int, (decimal, decimal)> { [1] = (0.0154m, 0.01694m) });

        copies.Keys.Should().BeEquivalentTo([1, 2, 4], "syrup and sugar don't depend on the egg");
        copies[2].CostPerBaseUnit.Should().Be(RawMaterialRules.StoredCost(0.0154m * 30m / 70m));
        copies[4].CostPerBaseUnit.Should().Be(RawMaterialRules.StoredCost((700m * copies[2].CostPerBaseUnit + 300m * 0.08m) / 1000m),
            "the custard is worked out from the egg white's new cost, not its old one");
        copies[4].Should().NotBeSameAs(custard);
        custard.CostPerBaseUnit.Should().Be(custardBefore, "what's given is never changed");
        egg.CostPerBaseUnit.Should().Be(0.014m);
    }

    [Test]
    public void Reprice_follows_a_link_to_a_link()
    {
        var egg = Purchased(1, 0.014m, 0.014m);
        var white = Linked(2, linkedToId: 1, weightShare: 70m, costShare: 30m);
        var whiteHalf = Linked(3, linkedToId: 2, weightShare: 50m, costShare: 50m);
        var materials = Index(egg, white, whiteHalf);

        var copies = IngredientCostRules.Reprice(materials, [], new Dictionary<int, (decimal, decimal)> { [1] = (0.028m, 0.028m) });

        copies[3].CostPerBaseUnit.Should().Be(copies[2].CostPerBaseUnit, "a half by weight that carries half the cost costs the same a gram");
        copies[2].CostPerBaseUnit.Should().Be(RawMaterialRules.StoredCost(0.028m * 30m / 70m));
    }

    [Test]
    public void A_change_reaches_the_material_and_everything_made_or_cut_from_it_and_nothing_else()
    {
        // Egg → egg white (linked) → custard (made from white and sugar); syrup is made from sugar alone.
        RawMaterial[] materials = [Purchased(1, 0.014m, 0.014m), Linked(2, linkedToId: 1, weightShare: 70m, costShare: 30m), Purchased(3, 0.08m, 0.08m), Made(4), Made(5)];
        IngredientBomLine[] lines = [Line(4, 2, 700m), Line(4, 3, 300m), Line(5, 3, 1000m)];

        IngredientCostRules.ReachedBy(materials, lines, 1).Should().BeEquivalentTo([1, 2, 4]);
        IngredientCostRules.ReachedBy(materials, lines, 3).Should().BeEquivalentTo([3, 4, 5]);
        IngredientCostRules.ReachedBy(materials, lines, 5).Should().BeEquivalentTo([5], "nothing is made from the syrup");
    }

    private static RawMaterial Purchased(int id, decimal cost, decimal buffered, decimal bufferPercent = 0m) => new()
    {
        Id = id, Name = $"Purchased {id}", Source = RawMaterialSource.Purchased, CostPerBaseUnit = cost, BufferedCostPerBaseUnit = buffered,
        CostBufferPercent = bufferPercent,
    };

    private static RawMaterial Made(int id, decimal weightLossPercent = 0m) =>
        new() { Id = id, Name = $"Made {id}", Source = RawMaterialSource.Made, WeightLossPercent = weightLossPercent };

    private static RawMaterial Linked(int id, int linkedToId, decimal weightShare, decimal costShare) => new()
    {
        Id = id, Name = $"Linked {id}", Source = RawMaterialSource.Linked, LinkedToId = linkedToId, WeightSharePercent = weightShare,
        CostSharePercent = costShare,
    };

    private static IngredientBomLine Line(int ingredientId, int componentId, decimal grams) =>
        new() { IngredientId = ingredientId, ComponentIngredientId = componentId, Grams = grams };

    private static Dictionary<int, RawMaterial> Index(params RawMaterial[] materials) => materials.ToDictionary(m => m.Id);
}
