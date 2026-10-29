using System.Globalization;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// The Price Simulator's costing changes (2026-10-29, at the user's request: try Wastage %, labor, overhead and VAT too),
/// tried on every product beside the price changes: a shop-wide Wastage % and a VAT % in place of today's, and labor,
/// overhead and administrative moved by percentage points, never below 0. Null leaves a figure as it is. Nothing is saved.
/// </summary>
public sealed record SimulatedCosting(
    decimal? WastagePercent, decimal? VatPercent, decimal? LaborPoints, decimal? OverheadPoints, decimal? AdministrativePoints)
{
    public bool IsEmpty => this is { WastagePercent: null, VatPercent: null, LaborPoints: null, OverheadPoints: null, AdministrativePoints: null };

    /// <summary>Refuses what a product's costing can't have: a Wastage % or VAT % below 0 or over 100.</summary>
    public void EnsureValid()
    {
        if (WastagePercent is { } wastage)
        {
            CostingRules.EnsureValidWastage(wastage);
        }

        if (VatPercent is < 0 or > 100)
        {
            throw new BusinessRuleException("The VAT to try must be from 0% to 100%.");
        }
    }

    /// <summary><paramref name="detail"/> with the changes put on its costing, as SavedProductReader.Build prices it.</summary>
    public ProductDetail Apply(ProductDetail detail)
    {
        if (IsEmpty)
        {
            return detail;
        }

        var costing = detail.Costing;
        return detail with
        {
            WastagePercent = WastagePercent ?? detail.WastagePercent,
            Costing = new CostingParameters
            {
                Id = costing?.Id ?? 0,
                ProductId = costing?.ProductId ?? 0,
                LaborPercent = Moved(costing?.LaborPercent ?? 0m, LaborPoints),
                OverheadPercent = Moved(costing?.OverheadPercent ?? 0m, OverheadPoints),
                AdministrativePercent = Moved(costing?.AdministrativePercent ?? 0m, AdministrativePoints),
                MarginPercent = costing?.MarginPercent ?? 0m,
                VatPercent = VatPercent ?? costing?.VatPercent ?? 0m,
            },
        };
    }

    /// <summary>
    /// The changes as the report lists them beside the price changes, each reaching every product: "Wastage % (shop-wide)"
    /// from <paramref name="wastagePercentNow"/> to the one tried, "VAT % (every product)" at the one tried, and the points
    /// labor, overhead and administrative move by.
    /// </summary>
    public IReadOnlyList<PrintedPriceChange> Printed(decimal wastagePercentNow)
    {
        List<PrintedPriceChange> printed = [];
        if (WastagePercent is { } wastage)
        {
            printed.Add(new PrintedPriceChange("Wastage % (shop-wide)", "%", wastagePercentNow, null, wastage));
        }

        if (VatPercent is { } vat)
        {
            printed.Add(new PrintedPriceChange("VAT % (every product)", "%", null, null, vat));
        }

        AddPoints("Labor %", LaborPoints);
        AddPoints("Overhead %", OverheadPoints);
        AddPoints("Administrative %", AdministrativePoints);
        return printed;

        void AddPoints(string name, decimal? points)
        {
            if (points is { } moved)
            {
                printed.Add(new PrintedPriceChange(
                    string.Create(CultureInfo.CurrentCulture, $"{name} {moved:+0.##;-0.##;0} points (every product)"), "points", null, null, moved));
            }
        }
    }

    private static decimal Moved(decimal percent, decimal? points) => points is { } by ? Math.Max(0m, percent + by) : percent;
}
