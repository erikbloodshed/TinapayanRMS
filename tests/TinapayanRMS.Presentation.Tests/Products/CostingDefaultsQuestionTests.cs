using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Presentation.Products;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>What Apply Costing Defaults asks: Settings' percentages, a category's own, or a line for each set the categories come to.</summary>
public class CostingDefaultsQuestionTests
{
    private static readonly CostingDefaults Defaults = new() { LaborPercent = 35m, OverheadPercent = 25m, AdministrativePercent = 15m, MarkupPercent = 30m };

    private static readonly ProductCategory Cakes = new() { Id = 1, Name = "Cakes", LaborPercent = 50m };

    private static readonly ProductCategory Breads = new() { Id = 2, Name = "Breads", WastagePercent = 5m };

    [Test]
    public void Products_whose_categories_set_none_of_their_own_are_asked_about_Settings_costing() =>
        CostingDefaultsQuestion.For("Pandesal and Mamon", [new Product { CategoryId = 2 }, new Product()], Defaults, [Cakes, Breads]).Should().Be(
            "Set the costing of Pandesal and Mamon to the costing defaults: labor 35%, overhead 25%, administrative 15% and markup 30%?"
            + "\n\nA version is recorded of each one this changes.");

    [Test]
    public void Products_of_one_category_with_its_own_are_asked_about_its_costing() =>
        CostingDefaultsQuestion.For("Ube Cake", [new Product { CategoryId = 1 }], Defaults, [Cakes, Breads]).Should().Be(
            "Set the costing of Ube Cake to the Cakes category's costing defaults: labor 50%, overhead 25%, administrative 15% and markup 30%?"
            + "\n\nA version is recorded of each one this changes.");

    [Test]
    public void A_category_that_comes_to_Settings_own_shares_its_line() =>
        CostingDefaultsQuestion.For(
            "3 products",
            [new Product { CategoryId = 1 }, new Product { CategoryId = 2 }, new Product()],
            Defaults,
            [Cakes, Breads]).Should().Be(
            "Set the costing of 3 products to their categories' costing defaults?\n\n"
            + "Cakes: labor 50%, overhead 25%, administrative 15% and markup 30%.\n"
            + "Breads and Uncategorized: labor 35%, overhead 25%, administrative 15% and markup 30%.\n\n"
            + "A version is recorded of each one this changes.");
}
