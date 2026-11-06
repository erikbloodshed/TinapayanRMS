using System.Globalization;
using System.Windows;
using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.UI.Converters;

namespace TinapayanRMS.UI.Tests.Converters;

/// <summary>Which icon a product's category name gets: categories are names users type, so it's read from keywords.</summary>
public class ProductCategoryIconConverterTests
{
    [Test]
    [Arguments("Rolls", BakeryKind.Roll)]
    [Arguments("Swiss Roll", BakeryKind.Roll)]
    [Arguments("Pianono", BakeryKind.Roll)]
    [Arguments("Payroll", BakeryKind.Other)]
    [Arguments("Cakes", BakeryKind.Cake)]
    [Arguments("Chiffon Cakes", BakeryKind.Cake)]
    [Arguments("Cheesecakes", BakeryKind.Cake)]
    [Arguments("Mamon", BakeryKind.Cake)]
    [Arguments("Pastries", BakeryKind.Pastry)]
    [Arguments("Croissants", BakeryKind.Pastry)]
    [Arguments("Pies & Tarts", BakeryKind.Pastry)]
    [Arguments("Hopia", BakeryKind.Pastry)]
    [Arguments("Breads", BakeryKind.Bread)]
    [Arguments("Loaves", BakeryKind.Loaf)]
    [Arguments("loaf breads", BakeryKind.Loaf)]
    [Arguments("Tinapay", BakeryKind.Bread)]
    [Arguments("Pandesal", BakeryKind.Bread)]
    [Arguments("Buns", BakeryKind.Bread)]
    [Arguments("Cookies", BakeryKind.Other)]
    [Arguments("Bundles", BakeryKind.Other)]
    [Arguments("Specials", BakeryKind.Other)]
    [Arguments("", BakeryKind.Other)]
    [Arguments(null, BakeryKind.Other)]
    public void A_category_name_picks_its_kind(string? categoryName, BakeryKind expected) =>
        ProductCategoryIconConverter.Classify(categoryName).Should().Be(expected);

    [Test]
    public void A_name_naming_two_kinds_is_a_roll_before_a_cake_before_a_pastry_before_a_bread()
    {
        ProductCategoryIconConverter.Classify("Roll Cakes").Should().Be(BakeryKind.Roll);
        ProductCategoryIconConverter.Classify("Cakes and Breads").Should().Be(BakeryKind.Cake);
        ProductCategoryIconConverter.Classify("Breads and Pastries").Should().Be(BakeryKind.Pastry);
    }

    [Test]
    public void A_composite_gets_the_composite_icon_whatever_its_category_and_any_other_product_its_categorys()
    {
        var composite = new DataTemplate();
        var bread = new DataTemplate();
        var converter = new ProductCategoryIconConverter { Composite = composite, Bread = bread };

        converter.Convert(new LinkingProduct(1, "Pasalubong Box", "Breads", IsComposite: true), typeof(DataTemplate), null!, CultureInfo.InvariantCulture)
            .Should().BeSameAs(composite);
        converter.Convert(new LinkingProduct(2, "Pandesal", "Breads"), typeof(DataTemplate), null!, CultureInfo.InvariantCulture)
            .Should().BeSameAs(bread);
    }
}
