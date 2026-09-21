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
    public void EnsureSectionsDistinct_accepts_one_in_each_role_without_names()
    {
        var act = () => SubAssemblyRules.EnsureSectionsDistinct([(1, "Dough", null), (2, "Filling", null), (3, "Topping", null)]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureSectionsDistinct_accepts_several_in_one_role_told_apart_by_name_with_one_unnamed()
    {
        var act = () => SubAssemblyRules.EnsureSectionsDistinct([(1, "Dough", null), (2, "Filling", null), (2, "Filling", "Cheese"), (2, "Filling", "Ube")]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureSectionsDistinct_rejects_two_unnamed_in_one_role()
    {
        var act = () => SubAssemblyRules.EnsureSectionsDistinct([(1, "Dough", null), (2, "Filling", null), (2, "Filling", "  ")]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("This recipe has more than one Filling without a name. Name them, such as Ube and Cheese, so their tabs can be told apart.");
    }

    [Fact]
    public void EnsureSectionsDistinct_rejects_one_name_twice_in_a_role_whatever_its_case_and_spacing()
    {
        var act = () => SubAssemblyRules.EnsureSectionsDistinct([(2, "Filling", "Ube"), (2, "Filling", " ube ")], "Cream Loaf");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cream Loaf has two Filling sub-assemblies called Ube. Give one of them another name.");
    }

    [Fact]
    public void EnsureValidOwnedNames_rejects_one_name_twice_even_in_different_roles()
    {
        var fine = () => SubAssemblyRules.EnsureValidOwnedNames([null, null, "Ube", "Cheese"]);
        var twice = () => SubAssemblyRules.EnsureValidOwnedNames(["Ube", "UBE"]);

        fine.Should().NotThrow("names are optional");
        twice.Should().Throw<InvalidOperationException>()
            .WithMessage("This product has two sub-assemblies of its own named Ube. Give one of them another name.");
    }

    [Fact]
    public void SectionLabel_is_a_products_own_name_and_a_linked_ones_name_or_else_its_owners()
    {
        var cheese = new SubAssembly { Name = "Cheese", OwnerProductId = 7, OwnerProduct = new Product { Id = 7, Name = "Cream Loaf" } };
        var unnamed = new SubAssembly { OwnerProductId = 7, OwnerProduct = new Product { Id = 7, Name = "Cream Loaf" } };
        var standalone = new SubAssembly { Name = "Ube Halaya" };

        SubAssemblyRules.SectionLabel(cheese, productId: 7).Should().Be("Cheese");
        SubAssemblyRules.SectionLabel(unnamed, productId: 7).Should().BeNull("a product's own needn't have a name");
        SubAssemblyRules.SectionLabel(unnamed, productId: 9).Should().Be("Cream Loaf", "a linked one without a name goes by its owner");
        SubAssemblyRules.SectionLabel(standalone, productId: 9).Should().Be("Ube Halaya");
    }

    [Fact]
    public void DisplayName_is_a_standalone_ones_name_and_a_products_own_role_as_that_products()
    {
        var dough = new SubAssemblyRole { Id = 1, Name = "Dough" };
        var cream = new Product { Id = 7, Name = "Cream Loaf" };
        var standalone = new SubAssembly { Name = "Variety Bread Dough", RoleId = 1, Role = dough };
        var owned = new SubAssembly { RoleId = 1, Role = dough, OwnerProductId = 7, OwnerProduct = cream };
        var named = new SubAssembly { Name = "Sweet", RoleId = 1, Role = dough, OwnerProductId = 7, OwnerProduct = cream };

        SubAssemblyRules.DisplayName(standalone).Should().Be("Variety Bread Dough");
        SubAssemblyRules.DisplayName(owned).Should().Be("Cream Loaf's Dough");
        SubAssemblyRules.DisplayName(named).Should().Be("Cream Loaf's Dough: Sweet");
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

    [Fact]
    public void EnsureValidPackagingLines_accepts_distinct_positive_lines()
    {
        var act = () => SubAssemblyRules.EnsureValidPackagingLines([(1, null, 1m), (2, null, 30m)]);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EnsureValidPackagingLines_rejects_a_quantity_that_isnt_positive(int qty)
    {
        var act = () => SubAssemblyRules.EnsureValidPackagingLines([(1, null, qty)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*quantity must be positive*");
    }

    [Fact]
    public void EnsureValidPackagingLines_rejects_no_lines_and_a_material_twice()
    {
        var none = () => SubAssemblyRules.EnsureValidPackagingLines([]);
        var twice = () => SubAssemblyRules.EnsureValidPackagingLines([(1, null, 1m), (1, null, 2m)]);

        none.Should().Throw<InvalidOperationException>().WithMessage("*at least one line*");
        twice.Should().Throw<InvalidOperationException>().WithMessage("*same material more than once*");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void EnsureKindUnchanged_refuses_switching_between_a_recipe_and_a_packaging_set(bool wasPackaging, bool isPackaging)
    {
        var act = () => SubAssemblyRules.EnsureKindUnchanged(wasPackaging, isPackaging);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureRecipeRole_refuses_the_packaging_role_by_its_flag_not_its_name()
    {
        var renamed = () => SubAssemblyRules.EnsureRecipeRole(new SubAssemblyRole { Name = "Boxes", IsPackaging = true });
        var named = () => SubAssemblyRules.EnsureRecipeRole(new SubAssemblyRole { Name = "Packaging" });

        renamed.Should().Throw<InvalidOperationException>().WithMessage("*Boxes role is for packaging sets*");
        named.Should().NotThrow();
    }
}
