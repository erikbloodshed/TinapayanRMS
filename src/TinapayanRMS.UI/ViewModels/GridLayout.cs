using System.Globalization;
using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>One condition of a column's filter, as the grid holds it (Syncfusion's FilterPredicate), its enums by name.</summary>
/// <param name="Value">The value compared with: a string, decimal, double, DateTime or bool, or null.</param>
public sealed record GridFilterCondition(
    string FilterType, string PredicateType, string FilterBehavior, bool IsCaseSensitive, string FilterMode, object? Value);

/// <summary>A column's filter: the conditions it holds, by the column's mapping name.</summary>
public sealed record GridColumnFilter(string Column, IReadOnlyList<GridFilterCondition> Conditions);

/// <summary>A column a grid is sorted by, in the order the sorts apply.</summary>
public sealed record GridSort(string Column, bool IsDescending);

/// <summary>
/// How a grid was left: the columns hidden, the sort and the filters. <see cref="HiddenColumns"/> is null when none was
/// ever chosen, so the grid keeps the columns its XAML hides.
/// </summary>
public sealed record GridLayout(IReadOnlyList<string>? HiddenColumns, IReadOnlyList<GridSort> Sorts, IReadOnlyList<GridColumnFilter> Filters)
{
    public static readonly GridLayout None = new(null, [], []);
}

/// <summary>
/// A grid's layout kept between runs for the Windows user (IViewStateStore), under <paramref name="key"/>: Reports' tables
/// keep their filters, sort and columns.
/// Controls/GridLayoutKeeping applies it to the grid and writes it back as it changes. A line it can't read back is left
/// out, so a layout written by a later version never stops the grid from showing.
/// </summary>
public sealed class GridLayoutStore(IViewStateStore viewState, string key)
{
    // Fields of a filter line, split by tab: column, filter type, predicate type, behaviour, case-sensitive, mode, value.
    private const char Separator = '\t';

    public GridLayout Read()
    {
        var hidden = viewState.ReadList($"{key}-hidden");
        var sorts = (viewState.ReadList($"{key}-sort") ?? []).Select(ReadSort).OfType<GridSort>().ToList();
        var filters = (viewState.ReadList($"{key}-filters") ?? [])
            .Select(ReadCondition)
            .OfType<(string Column, GridFilterCondition Condition)>()
            .GroupBy(line => line.Column)
            .Select(group => new GridColumnFilter(group.Key, [.. group.Select(line => line.Condition)]))
            .ToList();
        return new GridLayout(hidden, sorts, filters);
    }

    public void Write(GridLayout layout)
    {
        if (layout.HiddenColumns is { } hidden)
        {
            viewState.WriteList($"{key}-hidden", hidden);
        }

        viewState.WriteList($"{key}-sort", [.. layout.Sorts.Select(s => $"{s.Column}{Separator}{(s.IsDescending ? "desc" : "asc")}")]);
        viewState.WriteList($"{key}-filters",
        [
            .. layout.Filters.SelectMany(filter => filter.Conditions
                .Select(c => (Condition: c, Value: WriteValue(c.Value)))
                .Where(c => c.Value is not null)
                .Select(c => string.Join(Separator,
                    filter.Column, c.Condition.FilterType, c.Condition.PredicateType, c.Condition.FilterBehavior,
                    c.Condition.IsCaseSensitive ? "1" : "0", c.Condition.FilterMode, c.Value))),
        ]);
    }

    private static GridSort? ReadSort(string line) =>
        line.Split(Separator) is [var column, var direction] && direction is "asc" or "desc"
            ? new GridSort(column, direction == "desc")
            : null;

    private static (string Column, GridFilterCondition Condition)? ReadCondition(string line)
    {
        var fields = line.Split(Separator, 7);
        if (fields.Length != 7 || !TryReadValue(fields[6], out var value))
        {
            return null;
        }

        return (fields[0], new GridFilterCondition(fields[1], fields[2], fields[3], fields[4] == "1", fields[5], value));
    }

    /// <summary>A value as a type letter and its invariant text ("s:Breads", "m:12.5", "n" for null); null for a type it doesn't keep.</summary>
    private static string? WriteValue(object? value) => value switch
    {
        null => "n",
        string s => $"s:{s}",
        decimal m => $"m:{m.ToString(CultureInfo.InvariantCulture)}",
        double f => $"f:{f.ToString("R", CultureInfo.InvariantCulture)}",
        DateTime d => $"d:{d.ToString("O", CultureInfo.InvariantCulture)}",
        bool b => b ? "b:1" : "b:0",
        _ => null,
    };

    private static bool TryReadValue(string text, out object? value)
    {
        value = null;
        if (text == "n")
        {
            return true;
        }

        if (text.Length < 2 || text[1] != ':')
        {
            return false;
        }

        var body = text[2..];
        switch (text[0])
        {
            case 's':
                value = body;
                return true;
            case 'm' when decimal.TryParse(body, NumberStyles.Number, CultureInfo.InvariantCulture, out var m):
                value = m;
                return true;
            case 'f' when double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out var f):
                value = f;
                return true;
            case 'd' when DateTime.TryParse(body, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d):
                value = d;
                return true;
            case 'b':
                value = body == "1";
                return true;
            default:
                return false;
        }
    }
}
