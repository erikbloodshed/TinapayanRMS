using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;

namespace TinapayanRMS.UI.Converters;

/// <summary>What a product category's name says it holds, for its icon (<see cref="ProductCategoryIconConverter"/>).</summary>
public enum BakeryKind
{
    Other,
    Cake,
    Pastry,
    Bread,
}

/// <summary>
/// A product's icon from its category's name: the cake, pastry or bread drawing when the name says so (Cakes, Chiffon
/// Cakes, Pastries, Croissants, Breads, Loaf Breads, …), else <see cref="Other"/>. Categories are names users type, so
/// this reads keywords (in English and Filipino) rather than a stored choice. The templates are set where the
/// converter is declared.
/// </summary>
public sealed partial class ProductCategoryIconConverter : IValueConverter
{
    public DataTemplate? Cake { get; set; }

    public DataTemplate? Pastry { get; set; }

    public DataTemplate? Bread { get; set; }

    /// <summary>For an uncategorized product, or a category the keywords don't place.</summary>
    public DataTemplate? Other { get; set; }

    /// <summary>
    /// The kind a category's name names, checked cake, then pastry, then bread, so "Cakes and Breads" is a cake. A
    /// short keyword only counts as a whole word, or at a word's start, so "Bundles" isn't a bun.
    /// </summary>
    public static BakeryKind Classify(string? categoryName) =>
        string.IsNullOrWhiteSpace(categoryName) ? BakeryKind.Other
        : CakeKeywords().IsMatch(categoryName) ? BakeryKind.Cake
        : PastryKeywords().IsMatch(categoryName) ? BakeryKind.Pastry
        : BreadKeywords().IsMatch(categoryName) ? BakeryKind.Bread
        : BakeryKind.Other;

    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        Classify(value as string) switch
        {
            BakeryKind.Cake => Cake,
            BakeryKind.Pastry => Pastry,
            BakeryKind.Bread => Bread,
            _ => Other,
        };

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    [GeneratedRegex(@"cake|chiffon|\bmamon|\btorte|gateau|\bbrazo", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CakeKeywords();

    [GeneratedRegex(@"pastr|croissant|danish|\bpies?\b|\btarts?\b|\bpuffs?\b|turnover|empanada|hopia", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PastryKeywords();

    [GeneratedRegex(@"bread|tinapay|\bloa(f|ves)|\bbuns?\b|pandesal|pan de|brioche|baguette|ensaymada", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreadKeywords();
}
