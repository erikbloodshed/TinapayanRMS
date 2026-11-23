using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

/// <summary>
/// Which of a product's sections can go whole into another (ProductRules.EnsureValidFolds): into one of the recipe's, with
/// no piece size of its own, and never round into itself.
/// </summary>
public class ProductRulesFoldTests
{
    [Test]
    public void Sections_that_go_into_nothing_are_fine()
    {
        var act = () => ProductRules.EnsureValidFolds([(1, null, 60m, "Dough"), (2, null, 10m, "Filling")]);

        act.Should().NotThrow();
    }

    [Test]
    public void A_section_without_a_piece_size_can_go_whole_into_another()
    {
        var act = () => ProductRules.EnsureValidFolds([(1, null, 60m, "Dough"), (2, 1, 0m, "Sponge")]);

        act.Should().NotThrow();
    }

    [Test]
    public void A_chain_sums_through_and_is_fine()
    {
        // A new section's stand-in (-1) names it as a saved one's id does.
        var act = () => ProductRules.EnsureValidFolds([(1, null, 60m, "Dough"), (2, 1, 0m, "Sponge"), (-1, 2, 0m, "Starter")]);

        act.Should().NotThrow();
    }

    [Test]
    public void A_section_that_goes_into_another_cannot_have_a_piece_size_of_its_own()
    {
        var act = () => ProductRules.EnsureValidFolds([(1, null, 60m, "Dough"), (2, 1, 10m, "Sponge")]);

        act.Should().Throw<BusinessRuleException>().WithMessage("Sponge goes whole into Dough, so it can't have a weight per piece or yield of its own*");
    }

    [Test]
    public void A_section_cannot_go_into_one_the_recipe_does_not_have()
    {
        var act = () => ProductRules.EnsureValidFolds([(1, null, 60m, "Dough"), (2, 9, 0m, "Sponge")]);

        act.Should().Throw<BusinessRuleException>().WithMessage("Sponge goes into a section that isn't in this recipe any more*");
    }

    [Test]
    public void A_section_cannot_go_into_itself()
    {
        var act = () => ProductRules.EnsureValidFolds([(1, 1, 0m, "Dough")]);

        act.Should().Throw<BusinessRuleException>().WithMessage("Dough can't go into itself.");
    }

    [Test]
    public void Two_sections_cannot_go_into_each_other()
    {
        var act = () => ProductRules.EnsureValidFolds([(1, 2, 0m, "Dough"), (2, 1, 0m, "Sponge")]);

        act.Should().Throw<BusinessRuleException>().WithMessage("Dough goes into Sponge, which goes back into Dough*");
    }

    [Test]
    public void A_loop_through_others_is_refused_too()
    {
        var act = () => ProductRules.EnsureValidFolds([(1, 3, 0m, "Dough"), (2, 1, 0m, "Sponge"), (3, 2, 0m, "Starter")]);

        act.Should().Throw<BusinessRuleException>().WithMessage("*can't go into itself, even through others.");
    }

    [Test]
    public void A_chain_into_another_loop_is_refused_as_that_loop()
    {
        // The topping goes into the dough, which is in a loop with the sponge: the dough's loop is what's refused.
        var act = () => ProductRules.EnsureValidFolds([(1, 2, 0m, "Dough"), (2, 1, 0m, "Sponge"), (3, 1, 0m, "Topping")]);

        act.Should().Throw<BusinessRuleException>().WithMessage("Dough goes into Sponge*");
    }
}
