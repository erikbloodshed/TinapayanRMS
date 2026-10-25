using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.UI.Converters;

/// <summary>What a product category's name says it holds, for its icon (<see cref="ProductCategoryIconConverter"/>).</summary>
public enum BakeryKind
{
    Other,
    Roll,
    Cake,
    Pastry,
    Loaf,
    Bread,
}

/// <summary>
/// A Used By tile's icon, from its <see cref="LinkingProduct"/>: <see cref="Composite"/> for a composite, whatever its
/// category, else from its category's name: the roll cake, the cake, the pastry's cupcake, the loaf or the bread image
/// when the name says so (Rolls, Cakes, Chiffon Cakes, Pastries, Croissants, Loaves, Loaf Breads, Breads, …), else
/// <see cref="Other"/>.
/// Categories are names users type, so this reads keywords (in English and Filipino) rather than a stored choice. The
/// templates are set where the converter is declared.
/// </summary>
public sealed partial class ProductCategoryIconConverter : IValueConverter
{
    public DataTemplate? Roll { get; set; }

    public DataTemplate? Cake { get; set; }

    public DataTemplate? Pastry { get; set; }

    public DataTemplate? Loaf { get; set; }

    public DataTemplate? Bread { get; set; }

    public DataTemplate? Composite { get; set; }

    /// <summary>For an uncategorized product, or a category the keywords don't place.</summary>
    public DataTemplate? Other { get; set; }

    /// <summary>
    /// The kind a category's name names, checked roll, then cake, then pastry, then loaf, then bread, so "Roll Cakes" is
    /// a roll, "Cakes and Breads" a cake and "Loaf Breads" a loaf. A short keyword only counts as a whole word, or at a
    /// word's start, so "Bundles" isn't a bun and "Payroll" isn't a roll.
    /// </summary>
    public static BakeryKind Classify(string? categoryName) =>
        string.IsNullOrWhiteSpace(categoryName) ? BakeryKind.Other
        : RollKeywords().IsMatch(categoryName) ? BakeryKind.Roll
        : CakeKeywords().IsMatch(categoryName) ? BakeryKind.Cake
        : PastryKeywords().IsMatch(categoryName) ? BakeryKind.Pastry
        : LoafKeywords().IsMatch(categoryName) ? BakeryKind.Loaf
        : BreadKeywords().IsMatch(categoryName) ? BakeryKind.Bread
        : BakeryKind.Other;

    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is LinkingProduct { IsComposite: true } ? Composite
        : Classify((value as LinkingProduct)?.CategoryName) switch
        {
            BakeryKind.Roll => Roll,
            BakeryKind.Cake => Cake,
            BakeryKind.Pastry => Pastry,
            BakeryKind.Loaf => Loaf,
            BakeryKind.Bread => Bread,
            _ => Other,
        };

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    [GeneratedRegex(@"\brolls?\b|roulade|pianono", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RollKeywords();

    [GeneratedRegex(@"cake|chiffon|\bmamon|\btorte|gateau|\bbrazo", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CakeKeywords();

    [GeneratedRegex(@"pastr|croissant|danish|\bpies?\b|\btarts?\b|\bpuffs?\b|turnover|empanada|hopia", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PastryKeywords();

    [GeneratedRegex(@"\bloa(f|ves)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LoafKeywords();

    [GeneratedRegex(@"bread|tinapay|\bbuns?\b|pandesal|pan de|brioche|baguette|ensaymada", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreadKeywords();
}
