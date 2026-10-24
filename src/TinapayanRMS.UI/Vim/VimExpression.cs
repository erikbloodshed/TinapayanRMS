using System.Globalization;

namespace TinapayanRMS.UI.Vim;

/// <summary>
/// Works out the sum typed after Ctrl+R = (Vim's expression register): numbers, <c>+ - * /</c>, <c>%</c> for the
/// remainder, brackets, and a sign in front of a number or a bracket, with the usual precedence. Unlike Vim, division
/// is never whole-number division: 7/2 is 3.5, since costs and grams need the decimals. <c>.</c> is the decimal point,
/// and a comma between digits groups thousands (1,250).
/// </summary>
public static class VimExpression
{
    /// <summary>
    /// The value of <paramref name="text"/>, or null with <paramref name="error"/> saying why it can't be worked out:
    /// nothing typed, a character that's no part of a sum, a bracket missing, a division by zero, or a number too big.
    /// </summary>
    public static decimal? Evaluate(string text, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Type a sum, such as 5*(2+3), then Enter.";
            return null;
        }

        var parser = new Parser(text);
        try
        {
            var value = parser.Sum();
            parser.SkipSpaces();
            if (!parser.AtEnd)
            {
                error = parser.Current == ')'
                    ? $"Can't work out {text.Trim()}: a ( is missing."
                    : $"Can't work out {text.Trim()}: {parser.Current} isn't part of a sum.";
                return null;
            }

            return value;
        }
        catch (FormatException e)
        {
            error = $"Can't work out {text.Trim()}: {e.Message}";
        }
        catch (DivideByZeroException)
        {
            error = $"Can't work out {text.Trim()}: it divides by zero.";
        }
        catch (OverflowException)
        {
            error = $"Can't work out {text.Trim()}: the answer is too big.";
        }

        return null;
    }

    /// <summary>
    /// <paramref name="value"/> as it's typed into a box: rounded half away from zero to <paramref name="decimals"/> when
    /// given, with no trailing zeros, no thousands separators (a number box won't take them) and
    /// <paramref name="decimalSeparator"/> for the point.
    /// </summary>
    public static string Format(decimal value, int? decimals, string decimalSeparator = ".")
    {
        if (decimals is { } places)
        {
            value = Math.Round(value, Math.Clamp(places, 0, 28), MidpointRounding.AwayFromZero);
        }

        // "G29" drops the trailing zeros a decimal keeps (2.50m is "2.5"), and never uses an exponent for a decimal.
        var text = value.ToString("G29", CultureInfo.InvariantCulture);
        return decimalSeparator == "." ? text : text.Replace(".", decimalSeparator, StringComparison.Ordinal);
    }

    /// <summary>Recursive descent: a sum is terms added or taken away, a term is factors multiplied or divided.</summary>
    private sealed class Parser(string text)
    {
        private int _position;

        public bool AtEnd => _position >= text.Length;

        public char Current => text[_position];

        public decimal Sum()
        {
            var value = Term();
            while (true)
            {
                SkipSpaces();
                if (Take('+'))
                {
                    value += Term();
                }
                else if (Take('-'))
                {
                    value -= Term();
                }
                else
                {
                    return value;
                }
            }
        }

        public void SkipSpaces()
        {
            while (!AtEnd && char.IsWhiteSpace(Current))
            {
                _position++;
            }
        }

        private decimal Term()
        {
            var value = Factor();
            while (true)
            {
                SkipSpaces();
                if (Take('*'))
                {
                    value *= Factor();
                }
                else if (Take('/'))
                {
                    value /= Factor();
                }
                else if (Take('%'))
                {
                    value %= Factor();
                }
                else
                {
                    return value;
                }
            }
        }

        private decimal Factor()
        {
            SkipSpaces();
            if (AtEnd)
            {
                throw new FormatException("a number is missing at the end.");
            }

            if (Take('-'))
            {
                return -Factor();
            }

            if (Take('+'))
            {
                return Factor();
            }

            if (Take('('))
            {
                var value = Sum();
                SkipSpaces();
                if (!Take(')'))
                {
                    throw new FormatException("a ) is missing.");
                }

                return value;
            }

            return Number();
        }

        /// <summary>A number, which may group its thousands with commas between digits (1,250), as a pasted figure does.</summary>
        private decimal Number()
        {
            var start = _position;
            while (!AtEnd && (char.IsAsciiDigit(Current) || Current == '.' || IsGroupComma()))
            {
                _position++;
            }

            if (_position == start)
            {
                throw new FormatException($"{Current} isn't part of a sum.");
            }

            var number = text[start.._position].Replace(",", string.Empty, StringComparison.Ordinal);
            if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"{number} isn't a number.");
            }

            return value;
        }

        private bool IsGroupComma() =>
            Current == ',' && _position > 0 && char.IsAsciiDigit(text[_position - 1])
            && _position + 1 < text.Length && char.IsAsciiDigit(text[_position + 1]);

        private bool Take(char character)
        {
            if (!AtEnd && Current == character)
            {
                _position++;
                return true;
            }

            return false;
        }
    }
}
