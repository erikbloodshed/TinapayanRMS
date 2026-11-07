using AwesomeAssertions;
using TinapayanRMS.Components.Vim;

namespace TinapayanRMS.Components.Tests.Vim;

/// <summary>VimExpression: the sum typed after Ctrl+R =, and its answer as it's typed into a box.</summary>
public class VimExpressionTests
{
    [Test]
    [Arguments("5*(2+3)", "25")]
    [Arguments("2+3*4", "14")]
    [Arguments("(2+3)*4", "20")]
    [Arguments("10-4-3", "3")]
    [Arguments("100/10/2", "5")]
    [Arguments("-(2+3)", "-5")]
    [Arguments("-2*-3", "6")]
    [Arguments("+7", "7")]
    [Arguments("7/2", "3.5")]
    [Arguments("3.0/2.0", "1.5")]
    [Arguments(".5*4", "2")]
    [Arguments("7%3", "1")]
    [Arguments("1,250*2", "2500")]
    [Arguments("  5 * ( 2 + 3 )  ", "25")]
    public void Works_out_a_sum(string sum, string answer)
    {
        VimExpression.Evaluate(sum, out var error).Should().Be(decimal.Parse(answer, System.Globalization.CultureInfo.InvariantCulture));
        error.Should().BeEmpty();
    }

    [Test]
    [Arguments("", "Type a sum")]
    [Arguments("   ", "Type a sum")]
    [Arguments("5*", "a number is missing")]
    [Arguments("(2+3", "a ) is missing")]
    [Arguments("2+3)", "a ( is missing")]
    [Arguments("1/0", "divides by zero")]
    [Arguments("5%0", "divides by zero")]
    [Arguments("abc", "a isn't part of a sum")]
    [Arguments("2x3", "x isn't part of a sum")]
    [Arguments("1.2.3", "1.2.3 isn't a number")]
    [Arguments("79228162514264337593543950335*2", "too big")]
    public void Says_why_a_sum_cant_be_worked_out(string sum, string reason)
    {
        VimExpression.Evaluate(sum, out var error).Should().BeNull();
        error.Should().Contain(reason);
    }

    [Test]
    [Arguments("3.33333333", 4, "3.3333")]
    [Arguments("3.33333333", 0, "3")]
    [Arguments("2.5", 0, "3")]
    [Arguments("-2.5", 0, "-3")]
    [Arguments("2.50", null, "2.5")]
    [Arguments("25.00", 2, "25")]
    [Arguments("1250000", null, "1250000")]
    public void Formats_the_answer_as_it_is_typed(string value, int? decimals, string typed)
    {
        VimExpression.Format(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), decimals).Should().Be(typed);
    }

    [Test]
    public void Formats_the_answer_with_the_boxs_decimal_point()
    {
        VimExpression.Format(1.5m, 2, ",").Should().Be("1,5");
    }
}
