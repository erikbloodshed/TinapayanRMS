using AwesomeAssertions;
using TinapayanRMS.UI.Vim;

namespace TinapayanRMS.UI.Tests.Vim;

/// <summary>VimExpression: the sum typed after Ctrl+R =, and its answer as it's typed into a box.</summary>
public class VimExpressionTests
{
    [Theory]
    [InlineData("5*(2+3)", "25")]
    [InlineData("2+3*4", "14")]
    [InlineData("(2+3)*4", "20")]
    [InlineData("10-4-3", "3")]
    [InlineData("100/10/2", "5")]
    [InlineData("-(2+3)", "-5")]
    [InlineData("-2*-3", "6")]
    [InlineData("+7", "7")]
    [InlineData("7/2", "3.5")]
    [InlineData("3.0/2.0", "1.5")]
    [InlineData(".5*4", "2")]
    [InlineData("7%3", "1")]
    [InlineData("1,250*2", "2500")]
    [InlineData("  5 * ( 2 + 3 )  ", "25")]
    public void Works_out_a_sum(string sum, string answer)
    {
        VimExpression.Evaluate(sum, out var error).Should().Be(decimal.Parse(answer, System.Globalization.CultureInfo.InvariantCulture));
        error.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "Type a sum")]
    [InlineData("   ", "Type a sum")]
    [InlineData("5*", "a number is missing")]
    [InlineData("(2+3", "a ) is missing")]
    [InlineData("2+3)", "a ( is missing")]
    [InlineData("1/0", "divides by zero")]
    [InlineData("5%0", "divides by zero")]
    [InlineData("abc", "a isn't part of a sum")]
    [InlineData("2x3", "x isn't part of a sum")]
    [InlineData("1.2.3", "1.2.3 isn't a number")]
    [InlineData("79228162514264337593543950335*2", "too big")]
    public void Says_why_a_sum_cant_be_worked_out(string sum, string reason)
    {
        VimExpression.Evaluate(sum, out var error).Should().BeNull();
        error.Should().Contain(reason);
    }

    [Theory]
    [InlineData("3.33333333", 4, "3.3333")]
    [InlineData("3.33333333", 0, "3")]
    [InlineData("2.5", 0, "3")]
    [InlineData("-2.5", 0, "-3")]
    [InlineData("2.50", null, "2.5")]
    [InlineData("25.00", 2, "25")]
    [InlineData("1250000", null, "1250000")]
    public void Formats_the_answer_as_it_is_typed(string value, int? decimals, string typed)
    {
        VimExpression.Format(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), decimals).Should().Be(typed);
    }

    [Fact]
    public void Formats_the_answer_with_the_boxs_decimal_point()
    {
        VimExpression.Format(1.5m, 2, ",").Should().Be("1,5");
    }
}
