using System.Text.RegularExpressions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// The full-colour images that say what a product is (wwwroot/icons, the WPF app's Assets/Icons): on the Products list,
/// one per category (ProductTree's ProductIcon), and on the Used By tiles, from the category's name (the WPF app's
/// ProductCategoryIconConverter): a composite's bag of breads, a roll cake, a cake, a pastry's cupcake, a loaf or a bread,
/// or else the navigation menu's Products glyph.
/// </summary>
public static partial class ProductIcons
{
    public static string? ImageFor(ProductIcon icon, int size = 64) => icon switch
    {
        ProductIcon.Breads => $"icons/Bread_{size}px.png",
        ProductIcon.Loaves => $"icons/Loaf_{size}px.png",
        ProductIcon.Cakes => $"icons/Cake_{size}px.png",
        ProductIcon.Pastries => $"icons/Pastry_{size}px.png",
        ProductIcon.Rolls => $"icons/Roll_Cake_{size}px.png",
        ProductIcon.Composite => $"icons/Composite_{size}px.png",
        _ => null,
    };

    /// <summary>A Used By tile's image, or null for the Products glyph.</summary>
    public static string? ImageFor(LinkingProduct product, int size = 128) =>
        product.IsComposite ? $"icons/Composite_{size}px.png"
        : string.IsNullOrWhiteSpace(product.CategoryName) ? null
        : RollKeywords().IsMatch(product.CategoryName) ? $"icons/Roll_Cake_{size}px.png"
        : CakeKeywords().IsMatch(product.CategoryName) ? $"icons/Cake_{size}px.png"
        : PastryKeywords().IsMatch(product.CategoryName) ? $"icons/Pastry_{size}px.png"
        : LoafKeywords().IsMatch(product.CategoryName) ? $"icons/Loaf_{size}px.png"
        : BreadKeywords().IsMatch(product.CategoryName) ? $"icons/Bread_{size}px.png"
        : null;

    // Before cakes, so "Roll Cakes" is a roll.
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
