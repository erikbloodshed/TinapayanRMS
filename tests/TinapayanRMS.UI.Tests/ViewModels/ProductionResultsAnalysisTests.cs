using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>Reports' Production Results: the saved plans' results by product category, in pieces.</summary>
public class ProductionResultsAnalysisTests
{
    private static readonly ProductCategory Breads = new() { Id = 1, Name = "Breads" };
    private static readonly ProductCategory Cakes = new() { Id = 2, Name = "Cakes" };

    private static readonly Dictionary<int, Product> Products = new()
    {
        [10] = new Product { Id = 10, Name = "Pandesal", Category = Breads, QuantityRequired = 1 },
        [11] = new Product { Id = 11, Name = "Ensaymada Box of 8", Category = Breads, QuantityRequired = 8 },
        [12] = new Product { Id = 12, Name = "Chiffon", Category = Cakes, QuantityRequired = 1 },
        [13] = new Product { Id = 13, Name = "Cookie", QuantityRequired = 1 },
    };

    private static readonly Dictionary<int, decimal> Wastage = new() { [10] = 3m, [11] = 3m, [12] = 5m, [13] = 3m };

    [Fact]
    public void Results_add_up_by_category_in_pieces_and_leave_out_lines_with_nothing_made_typed()
    {
        SavedPlan[] plans =
        [
            Plan(1, Line(10, 1000m, made: 950m, left: 40m), Line(11, 10m, made: 10m), Line(12, 20m)),
            Plan(2, Line(10, 500m, made: 500m, left: 10m), Line(13, 100m, made: 90m)),
            Plan(3, Line(12, 10m, made: 9m, left: 1m)),
        ];

        var rows = ProductionResultsAnalysis.Analyse(plans, Products, Wastage);

        rows.Select(r => (r.Category, r.Plans, r.PlannedPieces, r.MadePieces, r.LeftoverPieces, r.WastagePercent)).Should().Equal(
            ("Breads", 2, 1580m, 1530m, 50m, 3m),
            ("Cakes", 1, 10m, 9m, 1m, 5m),
            ("Uncategorized", 1, 100m, 90m, 0m, 3m));

        // Breads: 50 of 1,580 short is 3.16%, a hair over the 3% wastage; Cakes: 1 of 10 is 10%, over its 5%.
        rows[0].ShortPercent.Should().BeApproximately(3.1646m, 0.0001m);
        rows[0].LeftoverPercent.Should().BeApproximately(3.2680m, 0.0001m);
        rows[0].IsShortOverWastage.Should().BeTrue();
        rows[1].Note.Should().Be("Short by more than its Wastage %");
    }

    [Fact]
    public void A_category_making_more_than_planned_is_short_by_less_than_0_and_not_over_its_wastage()
    {
        var rows = ProductionResultsAnalysis.Analyse([Plan(1, Line(10, 100m, made: 104m))], Products, Wastage);

        rows.Single().ShortPercent.Should().Be(-4m);
        rows.Single().IsShortOverWastage.Should().BeFalse();
        rows.Single().Note.Should().BeEmpty();
    }

    [Fact]
    public void A_deleted_product_is_left_out()
    {
        ProductionResultsAnalysis.Analyse([Plan(1, Line(99, 100m, made: 80m))], Products, Wastage).Should().BeEmpty();
    }

    private static SavedPlan Plan(int id, params SavedPlanLine[] lines) => new() { Id = id, Name = $"Plan {id}", Lines = [.. lines] };

    private static SavedPlanLine Line(int productId, decimal units, decimal? made = null, decimal? left = null) =>
        new() { ProductId = productId, Units = units, MadeUnits = made, LeftoverUnits = left };
}
