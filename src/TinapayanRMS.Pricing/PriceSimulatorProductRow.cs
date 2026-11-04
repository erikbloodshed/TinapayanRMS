using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Pricing;

/// <summary>A product on the Price Simulator screen: its cost, suggested price and markup now, and with the price changes tried.</summary>
public sealed class PriceSimulatorProductRow(Product product, ProductCostSummary now, ProductCostSummary simulated)
{
    public int ProductId => product.Id;

    public string ProductName => product.Name;

    public string Category => product.Category?.Name ?? ProductRules.UncategorizedTitle;

    /// <summary>The ingredients and packaging, before labor, overhead and administrative: the Products header's Material Cost.</summary>
    public decimal RawMaterialCost => now.Material;

    public decimal SimulatedRawMaterialCost => simulated.Material;

    /// <summary>How far the raw material cost moves, in percent.</summary>
    public decimal RawMaterialChangePercent => CostingRules.PercentOf(SimulatedRawMaterialCost - RawMaterialCost, RawMaterialCost);

    /// <summary>For one sellable unit, as everything on the row is.</summary>
    public decimal BaseCost => now.BaseCost;

    public decimal SimulatedBaseCost => simulated.BaseCost;

    /// <summary>How far the base cost moves, in percent.</summary>
    public decimal ChangePercent => CostingRules.PercentOf(SimulatedBaseCost - BaseCost, BaseCost);

    public decimal SuggestedPrice => now.SellingPrice;

    public decimal SimulatedSuggestedPrice => simulated.SellingPrice;

    /// <summary>What the shop charges now, with VAT, as entered on the product; null when none is.</summary>
    public decimal? CurrentPrice => now.CurrentPrice;

    /// <summary>What the current price earns now, as the Products header shows it; null without a current price.</summary>
    public decimal? MarkupPercent => now.CurrentMarkupPercent;

    /// <summary>What the same current price would earn with the changes.</summary>
    public decimal? SimulatedMarkupPercent => simulated.CurrentMarkupPercent;

    /// <summary>
    /// The price, with VAT, that earns today's markup with the changes (PriceChangeRules.PriceToKeepMarkup): the current
    /// price, for a product no change reaches. Null without a current price.
    /// </summary>
    public decimal? PriceToKeepMarkup => PriceChangeRules.PriceToKeepMarkup(now.CurrentPrice, BaseCost, SimulatedBaseCost);

    /// <summary>Whether any change reaches it. Exact: both figures come from the same arithmetic, so an untouched product's are equal.</summary>
    public bool IsAffected => SimulatedBaseCost != BaseCost;
}
