using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

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
    public void EnsureOnePerRole_accepts_one_sub_assembly_in_each_role()
    {
        var act = () => SubAssemblyRules.EnsureOnePerRole([(1, "Dough"), (2, "Filling"), (3, "Topping")]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureOnePerRole_rejects_two_in_one_role_and_names_it()
    {
        var act = () => SubAssemblyRules.EnsureOnePerRole([(1, "Dough"), (2, "Filling"), (1, "Dough")]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("This recipe has more than one sub-assembly in the Dough role. A product takes one in each role: give one of them another role, or take it out.");
    }

    [Fact]
    public void DisplayName_is_a_standalone_ones_name_and_a_products_own_role_as_that_products()
    {
        var dough = new SubAssemblyRole { Id = 1, Name = "Dough" };
        var standalone = new SubAssembly { Name = "Variety Bread Dough", RoleId = 1, Role = dough };
        var owned = new SubAssembly { Name = "Sweet Dough", RoleId = 1, Role = dough, OwnerProductId = 7, OwnerProduct = new Product { Id = 7, Name = "Cream Loaf" } };

        SubAssemblyRules.DisplayName(standalone).Should().Be("Variety Bread Dough");
        SubAssemblyRules.DisplayName(owned).Should().Be("Cream Loaf's Dough", "a product's own goes by its role, even with a name saved before names went");
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
    public void CalculateAbsolutePercent_is_the_lines_share_of_the_whole_batch()
    {
        // A 1,000 g glaze with no base: 150 g of butter is 15% of it, and the lines come to 100 between them.
        SubAssemblyRules.CalculateAbsolutePercent(150m, batchWeight: 1000m).Should().Be(15m);
        SubAssemblyRules.CalculateAbsolutePercent(850m, batchWeight: 1000m).Should().Be(85m);
        SubAssemblyRules.CalculateAbsolutePercent(150m, batchWeight: 0m).Should().BeNull("an empty batch has nothing to take a share of");
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
