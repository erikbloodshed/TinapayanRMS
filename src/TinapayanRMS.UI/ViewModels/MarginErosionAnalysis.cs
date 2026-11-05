using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing.Versions;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A row of Reports' Margin Erosion: a product's markup at its current price as it stood on a date, its latest
/// version saved by then priced at its costs then, against now.
/// </summary>
/// <param name="VersionNumber">The version it's compared with, the latest saved on or before the date.</param>
/// <param name="Change">The markup now less the markup then, in percentage points: below 0 when it dropped.</param>
public sealed record MarginErosionRow(
    int ProductId,
    string ProductName,
    string Stage,
    string Category,
    int VersionNumber,
    DateTime VersionSavedAt,
    decimal BaseCostThen,
    decimal BaseCostNow,
    decimal CurrentPriceThen,
    decimal CurrentPriceNow,
    decimal MarkupThen,
    decimal MarkupNow,
    decimal Change)
{
    /// <summary>The markup dropped by a hundredth of a point or more.</summary>
    public bool HasDropped => Math.Round(Change, 2) < 0m;
}

/// <summary>
/// Works out Margin Erosion: for each product with a version saved on or before the date and a current price both then and
/// now, its markup then (the version's own figures at its costs then, as the History tab's Then columns) against now.
/// </summary>
public static class MarginErosionAnalysis
{
    /// <param name="now">Every product as saved today, priced at buffered costs.</param>
    /// <param name="versionsAsOf">Each product's latest version saved on or before the date, by product.</param>
    public static IReadOnlyList<MarginErosionRow> Analyse(IReadOnlyList<SavedProduct> now, IReadOnlyDictionary<int, ProductVersion> versionsAsOf)
    {
        List<MarginErosionRow> rows = [];
        foreach (var saved in now.Where(s => !s.Product.IsDeleted))
        {
            if (!versionsAsOf.TryGetValue(saved.Product.Id, out var version))
            {
                continue;
            }

            var (productThen, detailThen) = ProductVersionSnapshot.ToDetail(saved.Product.Id, ProductVersionContent.FromJson(version.Content));
            var then = SavedProductReader.Build(productThen, detailThen, CostBasis.Buffered).Costs;
            if (then.CurrentMarkupPercent is not { } markupThen || saved.Costs.CurrentMarkupPercent is not { } markupNow)
            {
                continue;
            }

            rows.Add(new MarginErosionRow(
                saved.Product.Id,
                saved.Product.Name,
                ProductRules.StageTitle(saved.Product.Stage),
                saved.Product.Category?.Name ?? ProductRules.UncategorizedTitle,
                version.Number,
                version.SavedAt,
                then.BaseCost,
                saved.Costs.BaseCost,
                then.CurrentPrice!.Value,
                saved.Costs.CurrentPrice!.Value,
                markupThen,
                markupNow,
                markupNow - markupThen));
        }

        return [.. rows.OrderBy(r => r.Change)];
    }
}
