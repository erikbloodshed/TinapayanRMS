using AwesomeAssertions;
using TinapayanRMS.Presentation.Products;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// A costing metric's value box on the Products' Costing tab: an amount typed sets the percentage it comes to, and the
/// box keeps showing what was typed until the percentage or what it's a percentage of moves on.
/// </summary>
public class TypedAmountTests
{
    [Test]
    public void An_amount_sets_the_percentage_of_the_cost_it_comes_to()
    {
        TypedAmount? typed = null;

        var percent = TypedAmount.PercentFor(ref typed, 5m, ofCost: 20m, currentPercent: 10m);

        percent.Should().Be(25m, "₱5 is 25% of ₱20");
        typed.Should().Be(new TypedAmount(5m, 25m, 20m));
    }

    [Test]
    public void The_percentage_is_kept_to_the_four_decimals_it_is_saved_to()
    {
        TypedAmount? typed = null;

        TypedAmount.PercentFor(ref typed, 23.77m, ofCost: 134.09m, currentPercent: 0m).Should().Be(17.7269m);
    }

    [Test]
    public void With_no_cost_to_take_a_percentage_of_the_percentage_stays_and_nothing_is_remembered()
    {
        TypedAmount? typed = new(1m, 5m, 20m);

        TypedAmount.PercentFor(ref typed, 3m, ofCost: 0m, currentPercent: 12m).Should().Be(12m);
        typed.Should().BeNull();
    }

    [Test]
    public void The_box_shows_what_was_typed_while_its_percentage_and_cost_stand_and_the_worked_out_amount_after()
    {
        var typed = new TypedAmount(23.77m, 17.7269m, 134.09m);

        TypedAmount.Shown(typed, 17.7269m, 134.09m, workedOut: 23.7699m).Should().Be(23.77m);
        TypedAmount.Shown(typed, 20m, 134.09m, workedOut: 26.818m).Should().Be(26.818m, "the percentage was typed in its own box");
        TypedAmount.Shown(typed, 17.7269m, 150m, workedOut: 26.59m).Should().Be(26.59m, "the recipe's cost changed");
        TypedAmount.Shown(null, 17.7269m, 134.09m, workedOut: 23.7699m).Should().Be(23.7699m);
    }
}
