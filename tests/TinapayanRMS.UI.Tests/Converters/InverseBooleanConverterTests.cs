using System.Globalization;
using AwesomeAssertions;
using TinapayanRMS.UI.Converters;

namespace TinapayanRMS.UI.Tests.Converters;

/// <summary>A column's tick box on Reports' Columns list: ticked while the column isn't hidden, and hiding it when unticked.</summary>
public class InverseBooleanConverterTests
{
    private static readonly InverseBooleanConverter Converter = new();

    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public void A_column_is_ticked_while_it_isnt_hidden(bool isHidden, bool isChecked) =>
        Converter.Convert(isHidden, typeof(bool?), null!, CultureInfo.InvariantCulture).Should().Be(isChecked);

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(null, true)]
    public void Unticking_a_column_hides_it(bool? isChecked, bool isHidden) =>
        Converter.ConvertBack(isChecked, typeof(bool), null!, CultureInfo.InvariantCulture).Should().Be(isHidden);
}
