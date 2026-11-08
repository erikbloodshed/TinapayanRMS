using System.Globalization;
using AwesomeAssertions;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// The culture the app shows its numbers in (NumberCulture): the Windows region's, with the number symbols NumberField reads
/// whatever the region, so a box never shows a number in a form it then reads differently.
/// </summary>
public class NumberCultureTests
{
    [Test]
    public async Task A_region_with_a_comma_before_the_decimals_shows_numbers_with_a_point_and_keeps_its_dates()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");

        var culture = NumberCulture.Of(german);

        (1234.5m).ToString("N2", culture).Should().Be("1,234.50");
        (-5m).ToString("N2", culture).Should().Be("-5.00");
        (0.125m).ToString("P1", culture).Should().Be("12.5 %", "the region's own way of placing the percent sign stays");
        culture.DateTimeFormat.ShortDatePattern.Should().Be(german.DateTimeFormat.ShortDatePattern);
        culture.DateTimeFormat.GetMonthName(3).Should().Be("März");
        german.NumberFormat.NumberDecimalSeparator.Should().Be(",", "the region's own culture is left as it is");
    }

    [Test]
    [Arguments("en-PH")]
    [Arguments("en-US")]
    public async Task A_region_that_writes_numbers_as_the_app_does_is_kept_as_it_is(string name)
    {
        var region = CultureInfo.GetCultureInfo(name);

        NumberCulture.Of(region).Should().BeSameAs(region);
    }

    [Test]
    public async Task A_number_box_in_a_comma_region_reads_back_the_number_it_shows()
    {
        CultureInfo.CurrentCulture = NumberCulture.Of(CultureInfo.GetCultureInfo("de-DE"));
        await using var context = AppServices.NewContext();
        List<decimal> typed = [];
        var field = context.Render<NumberField<decimal>>(parameters => parameters.Add(f => f.Value, 1234.5m).Add(f => f.ValueChanged, typed.Add));

        field.Find("input").GetAttribute("value").Should().Be("1,234.50");
        field.Find("input").Change("1,234.50");
        field.Find("input").Change("12.5");

        typed.Should().Equal([12.5m], "the number shown reads back as itself, and 12.5 as twelve and a half, not 125");
    }
}
