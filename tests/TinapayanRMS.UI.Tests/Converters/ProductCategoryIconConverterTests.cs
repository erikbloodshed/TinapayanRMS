using System.Globalization;
using System.Windows;
using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.UI.Converters;

namespace TinapayanRMS.UI.Tests.Converters;

/// <summary>Which icon a product's category name gets: categories are names users type, so it's read from keywords.</summary>
public class ProductCategoryIconConverterTests
{
    [Theory]
    [InlineData("Rolls", BakeryKind.Roll)]
    [InlineData("Swiss Roll", BakeryKind.Roll)]
    [InlineData("Pianono", BakeryKind.Roll)]
    [InlineData("Payroll", BakeryKind.Other)]
    [InlineData("Cakes", BakeryKind.Cake)]
    [InlineData("Chiffon Cakes", BakeryKind.Cake)]
    [InlineData("Cheesecakes", BakeryKind.Cake)]
    [InlineData("Mamon", BakeryKind.Cake)]
    [InlineData("Pastries", BakeryKind.Pastry)]
    [InlineData("Croissants", BakeryKind.Pastry)]
    [InlineData("Pies & Tarts", BakeryKind.Pastry)]
    [InlineData("Hopia", BakeryKind.Pastry)]
    [InlineData("Breads", BakeryKind.Bread)]
    [InlineData("Loaves", BakeryKind.Loaf)]
    [InlineData("loaf breads", BakeryKind.Loaf)]
    [InlineData("Tinapay", BakeryKind.Bread)]
    [InlineData("Pandesal", BakeryKind.Bread)]
    [InlineData("Buns", BakeryKind.Bread)]
    [InlineData("Cookies", BakeryKind.Other)]
    [InlineData("Bundles", BakeryKind.Other)]
    [InlineData("Specials", BakeryKind.Other)]
    [InlineData("", BakeryKind.Other)]
    [InlineData(null, BakeryKind.Other)]
    public void A_category_name_picks_its_kind(string? categoryName, BakeryKind expected) =>
        ProductCategoryIconConverter.Classify(categoryName).Should().Be(expected);

    [Fact]
    public void A_name_naming_two_kinds_is_a_roll_before_a_cake_before_a_pastry_before_a_bread()
    {
        ProductCategoryIconConverter.Classify("Roll Cakes").Should().Be(BakeryKind.Roll);
        ProductCategoryIconConverter.Classify("Cakes and Breads").Should().Be(BakeryKind.Cake);
        ProductCategoryIconConverter.Classify("Breads and Pastries").Should().Be(BakeryKind.Pastry);
    }

    [Fact]
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
