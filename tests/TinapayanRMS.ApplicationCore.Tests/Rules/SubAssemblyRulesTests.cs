using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class SubAssemblyRulesTests
{
    public static IEnumerable<(decimal, decimal, decimal, decimal)> ScaledLines() =>
    [
        (1000m, 1200m, 5000m, 4166.6667m),
        (200m, 1200m, 5000m, 833.3333m),
        (650m, 1000m, 2000m, 1300m),
        (650m, 0m, 2000m, 650m),
    ];

    [Test]
    [MethodDataSource(nameof(ScaledLines))]
    public void ScaledGrams_scales_a_line_by_the_batchs_factor_to_the_stored_decimals(decimal grams, decimal from, decimal to, decimal expected) =>
        SubAssemblyRules.ScaledGrams(grams, from, to).Should().Be(expected);

    [Test]
    public void EnsureValidLines_rejects_an_empty_list()
    {
        var act = () => SubAssemblyRules.EnsureValidLines([]);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureValidLines_rejects_non_positive_grams()
    {
        var act = () => SubAssemblyRules.EnsureValidLines([(IngredientId: 1, Grams: 0m)]);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureValidLines_rejects_the_same_ingredient_twice()
    {
        var act = () => SubAssemblyRules.EnsureValidLines(
        [
            (IngredientId: 1, Grams: 100m),
            (IngredientId: 1, Grams: 50m),
        ]);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureValidLines_accepts_distinct_positive_lines()
    {
        var act = () => SubAssemblyRules.EnsureValidLines(
        [
            (IngredientId: 1, Grams: 1000m),
            (IngredientId: 2, Grams: 500m),
        ]);

        act.Should().NotThrow();
    }

    [Test]
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

    [Test]
    public void CalculateTotals_with_a_single_line_matches_that_line()
    {
        var (totalWeight, totalCost) = SubAssemblyRules.CalculateTotals(
        [
            (Grams: 250m, IngredientCostPerGram: 0.2m),
        ]);

        totalWeight.Should().Be(250m);
        totalCost.Should().Be(50m);
    }

    [Test]
    public void EnsureSectionsDistinct_accepts_one_in_each_role_without_names()
    {
        var act = () => SubAssemblyRules.EnsureSectionsDistinct([(1, "Dough", null), (2, "Filling", null), (3, "Topping", null)]);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureSectionsDistinct_accepts_several_in_one_role_told_apart_by_name_with_one_unnamed()
    {
        var act = () => SubAssemblyRules.EnsureSectionsDistinct([(1, "Dough", null), (2, "Filling", null), (2, "Filling", "Cheese"), (2, "Filling", "Ube")]);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureSectionsDistinct_rejects_two_unnamed_in_one_role()
    {
        var act = () => SubAssemblyRules.EnsureSectionsDistinct([(1, "Dough", null), (2, "Filling", null), (2, "Filling", "  ")]);

        act.Should().Throw<BusinessRuleException>()
            .WithMessage("This recipe has more than one Filling without a name. Name them, such as Ube and Cheese, so their tabs can be told apart.");
    }

    [Test]
    public void EnsureSectionsDistinct_rejects_one_name_twice_in_a_role_whatever_its_case_and_spacing()
    {
        var act = () => SubAssemblyRules.EnsureSectionsDistinct([(2, "Filling", "Ube"), (2, "Filling", " ube ")], "Cream Loaf");

        act.Should().Throw<BusinessRuleException>()
            .WithMessage("Cream Loaf has two Filling sub-assemblies called Ube. Give one of them another name.");
    }

    [Test]
    public void EnsureValidSectionName_takes_no_name_and_refuses_one_too_long()
    {
        var none = () => SubAssemblyRules.EnsureValidSectionName(null);
        var blank = () => SubAssemblyRules.EnsureValidSectionName("  ");
        var tooLong = () => SubAssemblyRules.EnsureValidSectionName(new string('a', SubAssemblyRules.MaxNameLength + 1));

        none.Should().NotThrow("names are optional");
        blank.Should().NotThrow();
        tooLong.Should().Throw<BusinessRuleException>().WithMessage("*keep it to 100 characters*");
    }

    [Test]
    public void StandaloneNameFor_is_the_product_and_role_with_the_sections_own_name_after_them()
    {
        SubAssemblyRules.StandaloneNameFor(" Cheese Roll ", "Dough", null).Should().Be("Cheese Roll Dough");
        SubAssemblyRules.StandaloneNameFor("Cheese Roll", "Filling", " Cheese ").Should().Be("Cheese Roll Filling: Cheese");
        SubAssemblyRules.StandaloneNameFor("Cheese Roll", "Dough", "dough").Should().Be("Cheese Roll Dough", "a name that's just the role's adds nothing");
        SubAssemblyRules.StandaloneNameFor("Cheese Roll", "Dough", "  ").Should().Be("Cheese Roll Dough");
    }

    [Test]
    public void UniqueStandaloneName_numbers_a_name_already_taken_whatever_its_case_and_spacing()
    {
        SubAssemblyRules.UniqueStandaloneName("Cheese Roll Dough", ["Pandesal Dough"]).Should().Be("Cheese Roll Dough");
        SubAssemblyRules.UniqueStandaloneName("Cheese Roll Dough", [" cheese roll dough "]).Should().Be("Cheese Roll Dough 2");
        SubAssemblyRules.UniqueStandaloneName("Cheese Roll Dough", ["Cheese Roll Dough", "CHEESE ROLL DOUGH 2"]).Should().Be("Cheese Roll Dough 3");
    }

    [Test]
    public void UniqueStandaloneName_cuts_a_long_name_to_fit_its_number()
    {
        var longName = new string('a', 95) + " Dough";

        var alone = SubAssemblyRules.UniqueStandaloneName(longName, []);
        var numbered = SubAssemblyRules.UniqueStandaloneName(longName, [alone]);

        alone.Should().HaveLength(SubAssemblyRules.MaxNameLength);
        numbered.Should().Be(new string('a', 95) + " Do 2");
    }

    [Test]
    [Arguments("Cheese Roll Filling: Cheese", "Cheese", true)]
    [Arguments("cheese roll filling", null, true)]
    [Arguments("Cheese Roll Filling 2", null, true)]
    [Arguments("Cheese Roll Filling: Cheese 3", "Cheese", true)]
    [Arguments("Cheese Roll Filling 1", null, false)]
    [Arguments("Cheese Roll Filling: Cheese", null, false)]
    [Arguments("Pan de Coco", null, false)]
    public void IsNamedAfter_knows_a_name_made_from_the_product_role_and_section_numbered_or_not(string name, string? sectionName, bool isNamedAfter) =>
        SubAssemblyRules.IsNamedAfter(name, "Cheese Roll", "Filling", sectionName).Should().Be(isNamedAfter);

    [Test]
    public void IsNamedAfter_knows_a_name_cut_short_to_fit_numbered_or_not()
    {
        var product = new string('a', 95);
        var name = SubAssemblyRules.StandaloneNameFor(product, "Dough", null);
        var alone = SubAssemblyRules.UniqueStandaloneName(name, []);
        var numbered = SubAssemblyRules.UniqueStandaloneName(name, [alone]);

        SubAssemblyRules.IsNamedAfter(alone, product, "Dough", null).Should().BeTrue($"{alone} is the name cut to fit");
        SubAssemblyRules.IsNamedAfter(numbered, product, "Dough", null).Should().BeTrue($"{numbered} is the name numbered and cut");
    }

    [Test]
    public void StoredSectionName_drops_nothing_and_the_roles_own_name()
    {
        SubAssemblyRules.StoredSectionName("  Cheese ", "Filling").Should().Be("Cheese");
        SubAssemblyRules.StoredSectionName("   ", "Filling").Should().BeNull();
        SubAssemblyRules.StoredSectionName("filling", "Filling").Should().BeNull("its name leaves the role's name out too");
    }

    [Test]
    public void LabelIn_reads_a_products_own_by_its_section_name_and_any_other_by_its_name()
    {
        var own = new SubAssembly { Name = "Cheese Roll Filling 2", OwnerProductId = 7, SectionName = null };
        var named = new SubAssembly { Name = "Cheese Roll Filling: Cheese", OwnerProductId = 7, SectionName = "Cheese" };
        var linked = new SubAssembly { Name = "Cheese Roll Filling: Ube", OwnerProductId = null };

        SubAssemblyRules.LabelIn(own, 7, "Cheese Roll", "Filling").Should().BeNull("a number its name was given isn't its label");
        SubAssemblyRules.LabelIn(named, 7, "Cheese Roll", "Filling").Should().Be("Cheese");
        SubAssemblyRules.LabelIn(named, 8, "Cheese Bun", "Filling").Should().Be("Cheese Roll Filling: Cheese", "a variant linking it reads its name");
        SubAssemblyRules.LabelIn(linked, 7, "Cheese Roll", "Filling").Should().Be("Ube");
    }

    [Test]
    [Arguments("Cheese Roll Filling", null)]
    [Arguments("Cheese Roll Filling: Cheese", "Cheese")]
    [Arguments("Cheese Roll Filling 2", null)]
    [Arguments("Pan de Coco", "Pan de Coco")]
    public void SectionNameFromSavedName_reads_the_label_its_name_gave_without_a_number(string name, string? sectionName) =>
        SubAssemblyRules.SectionNameFromSavedName(name, "Cheese Roll", "Filling").Should().Be(sectionName);

    [Test]
    public void EnsureLinkable_refuses_a_products_own_and_names_whose_it_is()
    {
        var own = () => SubAssemblyRules.EnsureLinkable("Cheese Roll Filling: Cheese", "Cheese Roll");
        var standalone = () => SubAssemblyRules.EnsureLinkable("Pan de Coco", null);

        own.Should().Throw<BusinessRuleException>().WithMessage("Cheese Roll Filling: Cheese is Cheese Roll's own, so it can't be linked. Copy it instead*");
        standalone.Should().NotThrow();
    }

    [Test]
    public void EnsureLinkableByMadeIngredient_takes_only_a_recipe_kept_on_the_Sub_Assemblies_screen()
    {
        var own = () => SubAssemblyRules.EnsureLinkableByMadeIngredient("Cheese Roll Filling: Cheese", "Cheese Roll", isPackaging: false);
        var set = () => SubAssemblyRules.EnsureLinkableByMadeIngredient("Gift Box Set", null, isPackaging: true);
        var standalone = () => SubAssemblyRules.EnsureLinkableByMadeIngredient("Butter Icing", null, isPackaging: false);

        own.Should().Throw<BusinessRuleException>().WithMessage("Cheese Roll Filling: Cheese is Cheese Roll's own, so a made ingredient can't be made from it*");
        set.Should().Throw<BusinessRuleException>().WithMessage("Gift Box Set is a packaging set*");
        standalone.Should().NotThrow();
    }

    [Test]
    public void EnsureNoMadeIngredientMadeFrom_names_the_made_ingredients()
    {
        var one = () => SubAssemblyRules.EnsureNoMadeIngredientMadeFrom("Butter Icing Base", ["Butter Icing"]);
        var two = () => SubAssemblyRules.EnsureNoMadeIngredientMadeFrom("Butter Icing Base", ["Butter Icing", "Ube Icing"]);
        var none = () => SubAssemblyRules.EnsureNoMadeIngredientMadeFrom("Butter Icing Base", []);

        one.Should().Throw<BusinessRuleException>().WithMessage("Butter Icing is made from Butter Icing Base, so it can't be deleted. On the Raw Materials screen, make it from another*");
        two.Should().Throw<BusinessRuleException>().WithMessage("Butter Icing, Ube Icing are made from Butter Icing Base*make them from another*");
        none.Should().NotThrow();
    }

    [Test]
    public void EnsureProductOwnedAllowed_refuses_only_a_product_owned_packaging_role()
    {
        var packaging = () => SubAssemblyRules.EnsureProductOwnedAllowed(new SubAssemblyRole { Name = "Packaging", IsPackaging = true, IsProductOwned = true });
        var filling = () => SubAssemblyRules.EnsureProductOwnedAllowed(new SubAssemblyRole { Name = "Filling", IsProductOwned = true });

        packaging.Should().Throw<BusinessRuleException>().WithMessage("*can't be product-owned*");
        filling.Should().NotThrow();
    }

    [Test]
    public void SectionLabel_is_a_sub_assemblys_name_less_the_product_and_role_it_was_saved_under_from_there()
    {
        SubAssemblyRules.SectionLabel("Cheese Roll Filling: Cheese", "Cheese Roll", "Filling").Should().Be("Cheese");
        SubAssemblyRules.SectionLabel("cheese roll filling: Cheese", " Cheese Roll ", "Filling").Should().Be("Cheese", "the name is matched as the rules match names");
        SubAssemblyRules.SectionLabel("cheese roll filling", "Cheese Roll", "Filling").Should().BeNull("saved from there without a name, it goes by its role alone");
        SubAssemblyRules.SectionLabel("Cheese Roll Filling 2", "Cheese Roll", "Filling").Should().Be("Cheese Roll Filling 2");
        SubAssemblyRules.SectionLabel("Cheese Roll Filling: Cheese", "Ube Roll", "Filling").Should().Be("Cheese Roll Filling: Cheese", "another product's goes by its whole name");
        SubAssemblyRules.SectionLabel("Ube Halaya", null, "Filling").Should().Be("Ube Halaya");
    }

    [Test]
    public void EnsureNotLinkedElsewhere_lets_an_unlinked_sub_assembly_go()
    {
        var act = () => SubAssemblyRules.EnsureNotLinkedElsewhere("Sweet Dough", []);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureNotLinkedElsewhere_names_every_product_linking_it()
    {
        var one = () => SubAssemblyRules.EnsureNotLinkedElsewhere("Sweet Dough", ["Cream Loaf Mini"]);
        var two = () => SubAssemblyRules.EnsureNotLinkedElsewhere("Sweet Dough", ["Ube Roll", "Cream Loaf Mini"]);

        one.Should().Throw<BusinessRuleException>().WithMessage("Cream Loaf Mini links Sweet Dough. Make a copy of it there, or remove it there, first.");
        two.Should().Throw<BusinessRuleException>().WithMessage("Cream Loaf Mini, Ube Roll link Sweet Dough.*");
    }

    [Test]
    public void EnsureStandaloneNameAvailable_accepts_a_free_name()
    {
        var act = () => SubAssemblyRules.EnsureStandaloneNameAvailable("Chocolate Glaze", ["Sweet Dough"]);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureStandaloneNameAvailable_rejects_a_taken_name_whatever_its_case_and_spacing()
    {
        var act = () => SubAssemblyRules.EnsureStandaloneNameAvailable(" sweet dough ", ["Sweet Dough"]);

        act.Should().Throw<BusinessRuleException>().WithMessage("There's already a sub-assembly named 'sweet dough'. Give this one its own name.");
    }

    [Test]
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

    [Test]
    public void FormulaBaseWeight_is_nothing_when_no_line_is_ticked()
    {
        SubAssemblyRules.FormulaBaseWeight([(Grams: 650m, IsFormulaBase: false)]).Should().Be(0m);
    }

    [Test]
    public void CalculateBakersPercent_is_the_lines_share_of_the_base()
    {
        // 1,000 g of flour is the base, so it reads 100 and 650 g of water reads 65.
        SubAssemblyRules.CalculateBakersPercent(1000m, baseWeight: 1000m).Should().Be(100m);
        SubAssemblyRules.CalculateBakersPercent(650m, baseWeight: 1000m).Should().Be(65m);
        SubAssemblyRules.CalculateBakersPercent(20m, baseWeight: 1000m).Should().Be(2m);
    }

    [Test]
    public void CalculateBakersPercent_has_no_figure_when_nothing_is_the_base()
    {
        // A glaze or a filling needn't have a base; no figure is the honest answer, not zero.
        SubAssemblyRules.CalculateBakersPercent(650m, baseWeight: 0m).Should().BeNull();
    }

    [Test]
    public void CalculateAbsolutePercent_is_the_lines_share_of_the_whole_batch()
    {
        // A 1,000 g glaze with no base: 150 g of butter is 15% of it, and the lines come to 100 between them.
        SubAssemblyRules.CalculateAbsolutePercent(150m, batchWeight: 1000m).Should().Be(15m);
        SubAssemblyRules.CalculateAbsolutePercent(850m, batchWeight: 1000m).Should().Be(85m);
        SubAssemblyRules.CalculateAbsolutePercent(150m, batchWeight: 0m).Should().BeNull("an empty batch has nothing to take a share of");
    }

    [Test]
    public void GramsFromBakersPercent_is_the_percentage_of_the_base()
    {
        // 65% on 1,000 g of flour is 650 g of water.
        SubAssemblyRules.GramsFromBakersPercent(65m, baseWeight: 1000m).Should().Be(650m);
        SubAssemblyRules.GramsFromBakersPercent(1.8m, baseWeight: 1450m).Should().Be(26.1m);
    }

    [Test]
    public void GramsFromBakersPercent_rounds_to_the_decimals_grams_are_stored_in()
    {
        // 2.333% of 1,015.5 g is 23.691615 g; stored grams have 4 decimals, so reopening the line gives back what was saved.
        SubAssemblyRules.GramsFromBakersPercent(2.333m, baseWeight: 1015.5m).Should().Be(23.6916m);
    }

    [Test]
    public void EnsureValidLines_refuses_a_base_line_given_as_a_percentage()
    {
        var act = () => SubAssemblyRules.EnsureValidLines(
            [new RecipeLine(IngredientId: 1, Grams: 1000m, IsFormulaBase: true, AuthoredBakersPercent: 100m)]);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureValidLines_refuses_a_percentage_that_isnt_positive()
    {
        var act = () => SubAssemblyRules.EnsureValidLines(
            [
                new RecipeLine(IngredientId: 1, Grams: 1000m, IsFormulaBase: true),
                new RecipeLine(IngredientId: 2, Grams: 650m, AuthoredBakersPercent: 0m),
            ]);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureValidPackagingLines_accepts_distinct_positive_lines()
    {
        var act = () => SubAssemblyRules.EnsureValidPackagingLines([(1, null, 1m), (2, null, 30m)]);

        act.Should().NotThrow();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public void EnsureValidPackagingLines_rejects_a_quantity_that_isnt_positive(int qty)
    {
        var act = () => SubAssemblyRules.EnsureValidPackagingLines([(1, null, qty)]);

        act.Should().Throw<BusinessRuleException>().WithMessage("*quantity must be positive*");
    }

    [Test]
    public void EnsureValidPackagingLines_rejects_no_lines_and_a_material_twice()
    {
        var none = () => SubAssemblyRules.EnsureValidPackagingLines([]);
        var twice = () => SubAssemblyRules.EnsureValidPackagingLines([(1, null, 1m), (1, null, 2m)]);

        none.Should().Throw<BusinessRuleException>().WithMessage("*at least one line*");
        twice.Should().Throw<BusinessRuleException>().WithMessage("*same material more than once*");
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public void EnsureKindUnchanged_refuses_switching_between_a_recipe_and_a_packaging_set(bool wasPackaging, bool isPackaging)
    {
        var act = () => SubAssemblyRules.EnsureKindUnchanged(wasPackaging, isPackaging);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureRecipeRole_refuses_the_packaging_role_by_its_flag_not_its_name()
    {
        var renamed = () => SubAssemblyRules.EnsureRecipeRole(new SubAssemblyRole { Name = "Boxes", IsPackaging = true });
        var named = () => SubAssemblyRules.EnsureRecipeRole(new SubAssemblyRole { Name = "Packaging" });

        renamed.Should().Throw<BusinessRuleException>().WithMessage("*Boxes role is for packaging sets*");
        named.Should().NotThrow();
    }
}
