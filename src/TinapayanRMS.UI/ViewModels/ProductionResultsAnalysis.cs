using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A row of Reports' Production Results (2026-10-29, at the user's request: record actual results): a product category's
/// saved plans with what was made typed in, in pieces, against the Wastage % its products are priced with.
/// </summary>
/// <param name="Plans">The saved plans with a result for one of its products.</param>
/// <param name="PlannedPieces">The pieces planned on the lines with a result.</param>
/// <param name="MadePieces">The pieces made on those lines.</param>
/// <param name="LeftoverPieces">The pieces left over unsold, of those made; lines without a left over count none.</param>
/// <param name="WastagePercent">The Wastage % its products are priced with: the category's own, or the shop-wide one.</param>
public sealed record ProductionResultRow(
    string Category,
    int Plans,
    decimal PlannedPieces,
    decimal MadePieces,
    decimal LeftoverPieces,
    decimal WastagePercent)
{
    /// <summary>How far short of the plan the pieces made fell, in percent of those planned; below 0 when more were made.</summary>
    public decimal ShortPercent => PlannedPieces == 0m ? 0m : (PlannedPieces - MadePieces) / PlannedPieces * 100m;

    /// <summary>The pieces left over, in percent of those made.</summary>
    public decimal LeftoverPercent => MadePieces == 0m ? 0m : LeftoverPieces / MadePieces * 100m;

    /// <summary>More fell short than the Wastage % allows for, by a tenth of a point or more: the wastage may be set too low.</summary>
    public bool IsShortOverWastage => Math.Round(ShortPercent - WastagePercent, 1) > 0m;

    /// <summary>The table's Note: says so when more fell short than the Wastage % allows for.</summary>
    public string Note => IsShortOverWastage ? "Short by more than its Wastage %" : string.Empty;
}

/// <summary>
/// Works out Production Results: every saved plan's lines with what was made typed in, added up by product category in
/// pieces (a Box of 8's units are 8 pieces each), so products sold by the box and by the piece add up alike. A line whose
/// product is deleted is left out, with no category to put it in.
/// </summary>
public static class ProductionResultsAnalysis
{
    public const string Uncategorized = "Uncategorized";

    /// <param name="products">The products as saved, by id, with their categories.</param>
    /// <param name="wastageByProduct">The Wastage % each product is priced with, by id.</param>
    public static IReadOnlyList<ProductionResultRow> Analyse(
        IReadOnlyList<SavedPlan> plans, IReadOnlyDictionary<int, Product> products, IReadOnlyDictionary<int, decimal> wastageByProduct)
    {
        var lines = plans
            .SelectMany(plan => plan.Lines.Select(line => (Plan: plan, Line: line)))
            .Where(p => p.Line.MadeUnits is not null && products.ContainsKey(p.Line.ProductId))
            .Select(p =>
            {
                var product = products[p.Line.ProductId];
                decimal Pieces(decimal units) => RecipeScalingRules.PiecesFor(units, product.QuantityRequired);
                return (
                    PlanId: p.Plan.Id,
                    Category: product.Category?.Name ?? Uncategorized,
                    Planned: Pieces(p.Line.Units),
                    Made: Pieces(p.Line.MadeUnits!.Value),
                    Left: Pieces(p.Line.LeftoverUnits ?? 0m),
                    Wastage: wastageByProduct.GetValueOrDefault(product.Id));
            });

        return
        [
            .. lines.GroupBy(l => l.Category, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => new ProductionResultRow(
                    g.First().Category,
                    g.Select(l => l.PlanId).Distinct().Count(),
                    g.Sum(l => l.Planned),
                    g.Sum(l => l.Made),
                    g.Sum(l => l.Left),
                    g.Max(l => l.Wastage)))
                .OrderBy(r => r.Category == Uncategorized)
                .ThenBy(r => r.Category, StringComparer.CurrentCultureIgnoreCase),
        ];
    }
}
