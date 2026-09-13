using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Tests.Rules;

public class IngredientCostGraphTests
{
    [Fact]
    public void GetDependentsInOrder_returns_empty_when_nothing_depends_on_it()
    {
        var componentIdsByIngredientId = new Dictionary<int, IReadOnlyList<int>>();

        var order = IngredientCostGraph.GetDependentsInOrder(1, componentIdsByIngredientId);

        order.Should().BeEmpty();
    }

    [Fact]
    public void GetDependentsInOrder_finds_a_direct_dependent()
    {
        // 2 (syrup) is made from 1 (sugar).
        var componentIdsByIngredientId = new Dictionary<int, IReadOnlyList<int>>
        {
            [2] = [1],
        };

        var order = IngredientCostGraph.GetDependentsInOrder(1, componentIdsByIngredientId);

        order.Should().Equal(2);
    }

    [Fact]
    public void GetDependentsInOrder_orders_a_multi_level_chain_components_first()
    {
        // 1 sugar -> 2 syrup -> 3 caramel. Sugar's price changes: syrup must be
        // recomputed before caramel, since caramel reads syrup's fresh CostPerBaseUnit.
        var componentIdsByIngredientId = new Dictionary<int, IReadOnlyList<int>>
        {
            [2] = [1],
            [3] = [2],
        };

        var order = IngredientCostGraph.GetDependentsInOrder(1, componentIdsByIngredientId);

        order.Should().Equal(2, 3);
    }

    [Fact]
    public void GetDependentsInOrder_handles_a_diamond_without_duplicating_a_node()
    {
        // 1 (butter) is used by both 2 (ganache) and 3 (frosting), and 4 (cake) uses both.
        var componentIdsByIngredientId = new Dictionary<int, IReadOnlyList<int>>
        {
            [2] = [1],
            [3] = [1],
            [4] = [2, 3],
        };

        var order = IngredientCostGraph.GetDependentsInOrder(1, componentIdsByIngredientId).ToList();

        order.Should().HaveCount(3);
        order.Should().Contain([2, 3, 4]);
        order.IndexOf(4).Should().BeGreaterThan(order.IndexOf(2));
        order.IndexOf(4).Should().BeGreaterThan(order.IndexOf(3));
    }

    [Fact]
    public void GetDependentsInOrder_ignores_made_ingredients_that_do_not_depend_on_it()
    {
        // 2 depends on 1; 4 depends on 3 — an unrelated chain that shouldn't appear.
        var componentIdsByIngredientId = new Dictionary<int, IReadOnlyList<int>>
        {
            [2] = [1],
            [4] = [3],
        };

        var order = IngredientCostGraph.GetDependentsInOrder(1, componentIdsByIngredientId);

        order.Should().Equal(2);
    }
}
