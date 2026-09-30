using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class CompositeProductRulesTests
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [1] = "Pasalubong Box",
        [2] = "Ensaymada",
        [3] = "Cheese Roll",
        [4] = "Party Package",
        [5] = "Gift Tray",
        [6] = "Merienda Set",
        [7] = "Office Pack",
    };

    private static readonly Func<int, string> NameOf = id => Names[id];

    [Fact]
    public void EnsureValidComponents_accepts_two_different_products()
    {
        var act = () => CompositeProductRules.EnsureValidComponents(1, [(2, 6, "Ensaymada"), (3, 4, "Cheese Roll")]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValidComponents_refuses_fewer_than_two_products()
    {
        CompositeProductRules.MinimumProducts.Should().Be(2);
        var none = () => CompositeProductRules.EnsureValidComponents(1, []);
        var one = () => CompositeProductRules.EnsureValidComponents(1, [(2, 6, "Ensaymada")]);

        none.Should().Throw<InvalidOperationException>().WithMessage("A composite product needs at least two different products.");
        one.Should().Throw<InvalidOperationException>().WithMessage("A composite product needs at least two different products.");
    }

    [Fact]
    public void EnsureValidComponents_refuses_a_product_on_two_lines_naming_it()
    {
        var act = () => CompositeProductRules.EnsureValidComponents(1, [(2, 6, "Ensaymada"), (3, 4, "Cheese Roll"), (2, 2, "Ensaymada")]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Ensaymada is on two lines. Put its quantity on one line and delete the other.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void EnsureValidComponents_refuses_a_quantity_under_one_naming_the_product(int quantity)
    {
        var act = () => CompositeProductRules.EnsureValidComponents(1, [(2, 6, "Ensaymada"), (3, quantity, "Cheese Roll")]);

        act.Should().Throw<InvalidOperationException>().WithMessage("Enter how many Cheese Roll go in: a whole number, 1 or more.");
    }

    [Fact]
    public void EnsureValidComponents_refuses_the_composite_itself()
    {
        var act = () => CompositeProductRules.EnsureValidComponents(1, [(1, 1, "Pasalubong Box"), (3, 4, "Cheese Roll")]);

        act.Should().Throw<InvalidOperationException>().WithMessage("A composite product can't contain itself.");
    }

    [Fact]
    public void EnsureValidComponents_lets_a_new_composite_hold_any_product()
    {
        // Not saved yet, so it has no id any of its products could share.
        var act = () => CompositeProductRules.EnsureValidComponents(0, [(2, 6, "Ensaymada"), (3, 4, "Cheese Roll")]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureNoCycle_refuses_a_composite_that_already_contains_this_one()
    {
        // The Party Package (4) already holds the Pasalubong Box (1): the box can't then hold the Party Package.
        var held = new Dictionary<int, IReadOnlyList<int>> { [4] = [1, 3] };

        var act = () => CompositeProductRules.EnsureNoCycle(1, [2, 4], held, NameOf);

        act.Should().Throw<InvalidOperationException>().WithMessage(
            "Party Package can't go into Pasalubong Box: it already contains Pasalubong Box, so Pasalubong Box would end up containing itself.");
    }

    [Fact]
    public void EnsureNoCycle_refuses_a_loop_through_another_composite()
    {
        // 4 holds 5, and 5 holds 1: putting 4 into 1 closes 1 -> 4 -> 5 -> 1.
        var held = new Dictionary<int, IReadOnlyList<int>> { [4] = [5, 3], [5] = [1, 2] };

        var act = () => CompositeProductRules.EnsureNoCycle(1, [4], held, NameOf);

        act.Should().Throw<InvalidOperationException>().WithMessage("Party Package can't go into Pasalubong Box*");
    }

    [Fact]
    public void EnsureNoCycle_refuses_the_composite_itself()
    {
        var act = () => CompositeProductRules.EnsureNoCycle(1, [1], new Dictionary<int, IReadOnlyList<int>>(), NameOf);

        act.Should().Throw<InvalidOperationException>().WithMessage("A composite product can't contain itself.");
    }

    [Fact]
    public void EnsureNoCycle_accepts_a_composite_holding_another_that_doesnt_contain_it()
    {
        // The Party Package (4) takes the Pasalubong Box (1), which holds only single products, and a composite (6) that
        // other composites hold too.
        var held = new Dictionary<int, IReadOnlyList<int>> { [1] = [2, 3], [6] = [2, 3], [7] = [6, 2] };

        var act = () => CompositeProductRules.EnsureNoCycle(4, [1, 6], held, NameOf);

        act.Should().NotThrow();
    }

    [Fact]
    public void Contains_follows_composites_inside_composites()
    {
        var held = new Dictionary<int, IReadOnlyList<int>> { [4] = [1, 3], [1] = [2, 3] };

        CompositeProductRules.Contains(4, 2, held).Should().BeTrue();
        CompositeProductRules.Contains(1, 4, held).Should().BeFalse();
        CompositeProductRules.Contains(2, 4, held).Should().BeFalse();
    }

    [Fact]
    public void EnsureKindUnchanged_refuses_either_change()
    {
        var becoming = () => CompositeProductRules.EnsureKindUnchanged(storedIsComposite: false, isComposite: true);
        var leaving = () => CompositeProductRules.EnsureKindUnchanged(storedIsComposite: true, isComposite: false);

        becoming.Should().Throw<InvalidOperationException>();
        leaving.Should().Throw<InvalidOperationException>();
        CompositeProductRules.EnsureKindUnchanged(true, true);
        CompositeProductRules.EnsureKindUnchanged(false, false);
    }

    [Fact]
    public void EnsureOneUnit_holds_a_composite_to_one_sellable_unit_only()
    {
        var composite = () => CompositeProductRules.EnsureOneUnit(isComposite: true, quantityRequired: 8);

        composite.Should().Throw<InvalidOperationException>();
        CompositeProductRules.EnsureOneUnit(isComposite: true, quantityRequired: 1);
        CompositeProductRules.EnsureOneUnit(isComposite: false, quantityRequired: 8);
    }

    [Fact]
    public void EnsureSectionRoles_accepts_product_owned_roles_and_no_sections_at_all()
    {
        CompositeProductRules.EnsureSectionRoles("Party Box", [("Topping", true), ("Filling", true)]);
        CompositeProductRules.EnsureSectionRoles("Party Box", []);
    }

    [Fact]
    public void EnsureSectionRoles_refuses_a_role_that_isnt_product_owned_naming_it()
    {
        var act = () => CompositeProductRules.EnsureSectionRoles("Party Box", [("Topping", true), ("Dough", false)]);

        act.Should().Throw<InvalidOperationException>().WithMessage(
            "Party Box can only have sections in product-owned roles, such as a topping or filling. Dough isn't one: tick Product-owned for it in Settings ▸ Roles, or take that section out.");
    }

    [Fact]
    public void A_line_costs_its_quantity_at_the_products_recipe_cost()
    {
        CompositeProductRules.CalculateComponentCost(12.40m, 6).Should().Be(74.40m);
    }

    [Fact]
    public void A_lines_packaging_is_its_quantity_at_the_products_packaging_cost()
    {
        CompositeProductRules.CalculateComponentPackagingCost(1.50m, 6).Should().Be(9m);
    }

    [Fact]
    public void EnsureComponentPackagingOnComposite_refuses_it_for_a_product_of_sub_assemblies_only()
    {
        var single = () => CompositeProductRules.EnsureComponentPackagingOnComposite(isComposite: false, includesComponentPackaging: true);

        single.Should().Throw<InvalidOperationException>().WithMessage("Only a composite product can include the packaging of the products in it.");
        CompositeProductRules.EnsureComponentPackagingOnComposite(isComposite: true, includesComponentPackaging: true);
        CompositeProductRules.EnsureComponentPackagingOnComposite(isComposite: true, includesComponentPackaging: false);
        CompositeProductRules.EnsureComponentPackagingOnComposite(isComposite: false, includesComponentPackaging: false);
    }
}
