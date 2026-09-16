using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
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
    public void EnsureStandaloneNameAvailable_accepts_a_free_name()
    {
        var act = () => SubAssemblyRules.EnsureStandaloneNameAvailable("Chocolate Glaze", ["Sweet Dough"]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureStandaloneNameAvailable_rejects_a_taken_name_whatever_its_case_and_spacing()
    {
        var act = () => SubAssemblyRules.EnsureStandaloneNameAvailable(" sweet dough ", ["Sweet Dough"]);

        act.Should().Throw<InvalidOperationException>().WithMessage("There's already a standalone sub-assembly named 'sweet dough'. Give this one its own name.");
    }

    [Fact]
    public void UniqueName_keeps_a_free_name_and_numbers_copies_of_a_taken_one()
    {
        SubAssemblyRules.UniqueName("Sweet Dough", ["Custard"]).Should().Be("Sweet Dough");
        SubAssemblyRules.UniqueName(" Sweet Dough ", ["sweet dough"]).Should().Be("Sweet Dough (copy)");
        SubAssemblyRules.UniqueName("Sweet Dough", ["Sweet Dough", "Sweet Dough (copy)"]).Should().Be("Sweet Dough (copy 2)");
    }

    [Fact]
    public void FormulaBaseWeight_adds_up_every_ticked_line()
    {
        // Two flours tick together and make 100% between them; the water doesn't count toward the base.
        SubAssemblyRules.FormulaBaseWeight(
        [
            (Grams: 700m, IsFormulaBase: true),
            (Grams: 300m, IsFormulaBase: true),
            (Grams: 650m, IsFormulaBase: false),
        ]).Should().Be(1000m);
    }

    [Fact]
    public void FormulaBaseWeight_is_nothing_when_no_line_is_ticked()
    {
        SubAssemblyRules.FormulaBaseWeight([(Grams: 650m, IsFormulaBase: false)]).Should().Be(0m);
    }

    [Fact]
    public void CalculateBakersPercent_is_the_lines_share_of_the_base()
    {
        // 1,000 g of flour is the base, so it reads 100 and 650 g of water reads 65.
        SubAssemblyRules.CalculateBakersPercent(1000m, baseWeight: 1000m).Should().Be(100m);
        SubAssemblyRules.CalculateBakersPercent(650m, baseWeight: 1000m).Should().Be(65m);
        SubAssemblyRules.CalculateBakersPercent(20m, baseWeight: 1000m).Should().Be(2m);
    }

    [Fact]
    public void CalculateBakersPercent_has_no_figure_when_nothing_is_the_base()
    {
        // A glaze or a filling needn't have a base; no figure is the honest answer, not zero.
        SubAssemblyRules.CalculateBakersPercent(650m, baseWeight: 0m).Should().BeNull();
    }

    [Fact]
    public void GramsFromBakersPercent_is_the_percentage_of_the_base()
    {
        // 65% on 1,000 g of flour is 650 g of water.
        SubAssemblyRules.GramsFromBakersPercent(65m, baseWeight: 1000m).Should().Be(650m);
        SubAssemblyRules.GramsFromBakersPercent(1.8m, baseWeight: 1450m).Should().Be(26.1m);
    }

    [Fact]
    public void GramsFromBakersPercent_rounds_to_the_decimals_grams_are_stored_in()
    {
        // 2.333% of 1,015.5 g is 23.691615 g; stored grams have 4 decimals, so reopening the line gives back what was saved.
        SubAssemblyRules.GramsFromBakersPercent(2.333m, baseWeight: 1015.5m).Should().Be(23.6916m);
    }

    [Fact]
    public void EnsureValidLines_refuses_a_base_line_given_as_a_percentage()
    {
        var act = () => SubAssemblyRules.EnsureValidLines(
            [new RecipeLine(IngredientId: 1, Grams: 1000m, IsFormulaBase: true, AuthoredBakersPercent: 100m)]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidLines_refuses_a_percentage_that_isnt_positive()
    {
        var act = () => SubAssemblyRules.EnsureValidLines(
            [
                new RecipeLine(IngredientId: 1, Grams: 1000m, IsFormulaBase: true),
                new RecipeLine(IngredientId: 2, Grams: 650m, AuthoredBakersPercent: 0m),
            ]);

        act.Should().Throw<InvalidOperationException>();
    }
}
