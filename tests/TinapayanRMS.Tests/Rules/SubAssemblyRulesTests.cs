using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Tests.Rules;

public class SubAssemblyRulesTests
{
    [Fact]
    public void EnsureValidLines_rejects_an_empty_list()
    {
        var act = () => SubAssemblyRules.EnsureValidLines([]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidLines_rejects_non_positive_grams()
    {
        var act = () => SubAssemblyRules.EnsureValidLines([(IngredientId: 1, Grams: 0m)]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidLines_rejects_the_same_ingredient_twice()
    {
        var act = () => SubAssemblyRules.EnsureValidLines(
        [
            (IngredientId: 1, Grams: 100m),
            (IngredientId: 1, Grams: 50m),
        ]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidLines_accepts_distinct_positive_lines()
    {
        var act = () => SubAssemblyRules.EnsureValidLines(
        [
            (IngredientId: 1, Grams: 1000m),
            (IngredientId: 2, Grams: 500m),
        ]);

        act.Should().NotThrow();
    }

    [Fact]
    public void CalculateTotals_sums_weight_and_weighted_cost()
    {
        // 800g flour @ ₱0.05/g (₱40) + 200g sugar @ ₱0.09/g (₱18).
        var (totalWeight, totalCost) = SubAssemblyRules.CalculateTotals(
        [
            (Grams: 800m, IngredientCostPerGram: 0.05m),
            (Grams: 200m, IngredientCostPerGram: 0.09m),
        ]);

        totalWeight.Should().Be(1000m);
        totalCost.Should().Be(58m);
    }

    [Fact]
    public void CalculateTotals_with_a_single_line_matches_that_line()
    {
        var (totalWeight, totalCost) = SubAssemblyRules.CalculateTotals(
        [
            (Grams: 250m, IngredientCostPerGram: 0.2m),
        ]);

        totalWeight.Should().Be(250m);
        totalCost.Should().Be(50m);
    }

    [Fact]
    public void EnsureValidOwnedNames_accepts_different_names()
    {
        var act = () => SubAssemblyRules.EnsureValidOwnedNames(["Sweet Dough", "Custard"]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValidOwnedNames_rejects_a_blank_name()
    {
        var act = () => SubAssemblyRules.EnsureValidOwnedNames(["Sweet Dough", "  "]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*needs a name*");
    }

    [Fact]
    public void EnsureValidOwnedNames_rejects_the_same_name_twice_whatever_its_case_and_spacing()
    {
        var act = () => SubAssemblyRules.EnsureValidOwnedNames(["Sweet Dough", " sweet dough"]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*already has a sub-assembly named 'Sweet Dough'*");
    }

    [Fact]
    public void EnsureNotLinkedElsewhere_lets_an_unlinked_sub_assembly_go()
    {
        var act = () => SubAssemblyRules.EnsureNotLinkedElsewhere("Sweet Dough", []);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureNotLinkedElsewhere_names_every_product_linking_it()
    {
        var one = () => SubAssemblyRules.EnsureNotLinkedElsewhere("Sweet Dough", ["Cream Loaf Mini"]);
        var two = () => SubAssemblyRules.EnsureNotLinkedElsewhere("Sweet Dough", ["Ube Roll", "Cream Loaf Mini"]);

        one.Should().Throw<InvalidOperationException>().WithMessage("Cream Loaf Mini links Sweet Dough. Make a copy of it there, or remove it there, first.");
        two.Should().Throw<InvalidOperationException>().WithMessage("Cream Loaf Mini, Ube Roll link Sweet Dough.*");
    }

    [Fact]
    public void UniqueName_keeps_a_free_name_and_numbers_copies_of_a_taken_one()
    {
        SubAssemblyRules.UniqueName("Sweet Dough", ["Custard"]).Should().Be("Sweet Dough");
        SubAssemblyRules.UniqueName(" Sweet Dough ", ["sweet dough"]).Should().Be("Sweet Dough (copy)");
        SubAssemblyRules.UniqueName("Sweet Dough", ["Sweet Dough", "Sweet Dough (copy)"]).Should().Be("Sweet Dough (copy 2)");
    }
}
