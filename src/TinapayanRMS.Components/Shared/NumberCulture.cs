using System.Globalization;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// The culture the app shows its numbers in: the Windows region's, for its dates and its names of days and months, with a
/// point before the decimals, commas between the thousands and a plain minus, whatever the region uses. NumberField reads a
/// typed number that way, and so do the Vim keys' sums (VimExpression), whatever the region; a region with a comma before
/// the decimals had a box show 12,50 and read 12,5 typed in it as 125.
/// </summary>
public static class NumberCulture
{
    /// <summary><paramref name="culture"/> with the app's number symbols: itself when it has them already.</summary>
    public static CultureInfo Of(CultureInfo culture)
    {
        var numbers = culture.NumberFormat;
        if (numbers is { NumberDecimalSeparator: ".", NumberGroupSeparator: ",", CurrencyDecimalSeparator: ".", CurrencyGroupSeparator: ",", PercentDecimalSeparator: ".", PercentGroupSeparator: ",", NegativeSign: "-", PositiveSign: "+" })
        {
            return culture;
        }

        var copy = (CultureInfo)culture.Clone();
        var format = copy.NumberFormat;
        format.NumberDecimalSeparator = ".";
        format.NumberGroupSeparator = ",";
        format.CurrencyDecimalSeparator = ".";
        format.CurrencyGroupSeparator = ",";
        format.PercentDecimalSeparator = ".";
        format.PercentGroupSeparator = ",";
        format.NegativeSign = "-";
        format.PositiveSign = "+";
        return copy;
    }

    /// <summary>Makes the app's culture the region's with the app's number symbols, on this thread and on every thread from now on.</summary>
    public static void Apply()
    {
        var culture = Of(CultureInfo.CurrentCulture);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
    }
}
