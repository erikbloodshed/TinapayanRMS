using AwesomeAssertions;
using TinapayanRMS.UI.Converters;

namespace TinapayanRMS.Tests.Converters;

/// <summary>Which icon a product's category name gets: categories are names users type, so it's read from keywords.</summary>
public class ProductCategoryIconConverterTests
{
    [Theory]
    [InlineData("Cakes", BakeryKind.Cake)]
    [InlineData("Chiffon Cakes", BakeryKind.Cake)]
    [InlineData("Cheesecakes", BakeryKind.Cake)]
    [InlineData("Mamon", BakeryKind.Cake)]
    [InlineData("Pastries", BakeryKind.Pastry)]
    [InlineData("Croissants", BakeryKind.Pastry)]
    [InlineData("Pies & Tarts", BakeryKind.Pastry)]
    [InlineData("Hopia", BakeryKind.Pastry)]
    [InlineData("Breads", BakeryKind.Bread)]
    [InlineData("loaf breads", BakeryKind.Bread)]
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
    public void A_name_naming_two_kinds_is_a_cake_before_a_pastry_before_a_bread()
    {
        ProductCategoryIconConverter.Classify("Cakes and Breads").Should().Be(BakeryKind.Cake);
        ProductCategoryIconConverter.Classify("Breads and Pastries").Should().Be(BakeryKind.Pastry);
    }
}
