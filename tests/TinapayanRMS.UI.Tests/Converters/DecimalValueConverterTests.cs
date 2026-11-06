using System.Globalization;
using System.Windows;
using AwesomeAssertions;
using TinapayanRMS.UI.Converters;

namespace TinapayanRMS.UI.Tests.Converters;

/// <summary>
/// A number box's double into a decimal property, as the number it shows. Since .NET 11 a plain conversion keeps the
/// double's exact binary value (28.35 is 28.349999999999997868371792719), which these would all fail on.
/// </summary>
public class DecimalValueConverterTests
{
    private static readonly DecimalValueConverter Converter = new();

    public static IEnumerable<(double, decimal)> TypedNumbers() =>
    [
        (28.35, 28.35m),
        (6.67, 6.67m),
        (0.0525, 0.0525m),
        (453.5924, 453.5924m),
        (0.1, 0.1m),
        (-12.5, -12.5m),
        (0, 0m),
        (1250, 1250m),
        (0.00001, 0.00001m),
    ];

    [Test]
    [MethodDataSource(nameof(TypedNumbers))]
    public void A_typed_number_goes_in_as_the_decimal_it_shows(double typed, decimal expected)
    {
        Converter.ConvertBack(typed, typeof(decimal), null, CultureInfo.InvariantCulture).Should().Be(expected);
        Converter.ConvertBack(typed, typeof(decimal?), null, CultureInfo.InvariantCulture).Should().Be(expected);
    }

    [Test]
    public void An_emptied_box_leaves_its_property_empty() =>
        Converter.ConvertBack(null, typeof(decimal?), null, CultureInfo.InvariantCulture).Should().BeNull();

    [Test]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    [Arguments(1e30)]
    public void A_number_a_decimal_cant_hold_leaves_the_property_as_it_was(double typed) =>
        Converter.ConvertBack(typed, typeof(decimal), null, CultureInfo.InvariantCulture).Should().Be(DependencyProperty.UnsetValue);

    [Test]
    public void A_property_that_isnt_a_decimal_gets_the_number_as_it_is() =>
        Converter.ConvertBack(28.35, typeof(double), null, CultureInfo.InvariantCulture).Should().Be(28.35);

    [Test]
    public void A_decimal_shows_in_the_box_as_its_double()
    {
        Converter.Convert(28.35m, typeof(double?), null, CultureInfo.InvariantCulture).Should().Be(28.35);
        Converter.Convert(null, typeof(double?), null, CultureInfo.InvariantCulture).Should().BeNull();
    }
}
