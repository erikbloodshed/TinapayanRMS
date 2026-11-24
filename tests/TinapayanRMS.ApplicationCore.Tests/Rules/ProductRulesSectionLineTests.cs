using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

/// <summary>
/// A product's sections on each other's lines (ProductRules.EnsureValidSectionLines): one of the recipe's, never the section
/// it's on, once there, with grams, and never round in a loop.
/// </summary>
public class ProductRulesSectionLineTests
{
    private static (int, string, IReadOnlyList<(int, decimal)>) Section(int id, string name, params (int UsedId, decimal Grams)[] lines) => (id, name, lines);

    [Test]
    public void Sections_of_ingredients_alone_are_fine()
    {
        var act = () => ProductRules.EnsureValidSectionLines([Section(1, "Dough"), Section(2, "Filling")]);

        act.Should().NotThrow();
    }

    [Test]
    public void A_section_can_be_on_several_others_lines_and_a_chain_is_fine()
    {
        // A new section's stand-in (-1) names it as a saved one's id does.
        var act = () => ProductRules.EnsureValidSectionLines(
        [
            Section(1, "Dough", (2, 400m)),
            Section(2, "Sponge", (-1, 50m)),
            Section(-1, "Starter"),
            Section(3, "Topping", (2, 20m)),
        ]);

        act.Should().NotThrow();
    }

    [Test]
    public void A_section_the_recipe_does_not_have_is_refused()
    {
        var act = () => ProductRules.EnsureValidSectionLines([Section(1, "Dough", (9, 400m))]);

        act.Should().Throw<BusinessRuleException>().WithMessage("Dough uses a section that isn't in this recipe any more. Delete its line.");
    }

    [Test]
    public void A_section_on_its_own_lines_is_refused()
    {
        var act = () => ProductRules.EnsureValidSectionLines([Section(1, "Dough", (1, 400m))]);

        act.Should().Throw<BusinessRuleException>().WithMessage("Dough can't be on its own lines.");
    }

    [Test]
    public void A_loop_through_others_is_refused()
    {
        var act = () => ProductRules.EnsureValidSectionLines(
        [
            Section(1, "Dough", (2, 400m)),
            Section(2, "Sponge", (3, 50m)),
            Section(3, "Starter", (1, 10m)),
        ]);

        act.Should().Throw<BusinessRuleException>().WithMessage("Dough uses Sponge, which uses Dough. A section can't be on its own lines, even through others.");
    }

    [Test]
    public void A_section_on_two_lines_of_one_is_refused()
    {
        var act = () => ProductRules.EnsureValidSectionLines([Section(1, "Dough", (2, 400m), (2, 100m)), Section(2, "Sponge")]);

        act.Should().Throw<BusinessRuleException>().WithMessage("Sponge is on two lines of Dough. Put its total on one line and delete the other.");
    }

    [Test]
    public void A_line_with_no_grams_is_refused()
    {
        var act = () => ProductRules.EnsureValidSectionLines([Section(1, "Dough", (2, 0m)), Section(2, "Sponge")]);

        act.Should().Throw<BusinessRuleException>().WithMessage("Enter how many grams of Sponge go into Dough, or delete its line.");
    }
}
