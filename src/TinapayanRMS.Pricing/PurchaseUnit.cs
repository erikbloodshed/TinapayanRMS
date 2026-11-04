using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Pricing;

/// <summary>
/// What a purchased material is bought in, as the Price Simulator and the Recipe API's /materials talk about it: the unit of
/// the brand price its cost comes from (its preferred brand's, else the dearest: RawMaterialRules.ApplyBrandPrices), and
/// that price; its base unit and no price while it has none.
/// </summary>
/// <param name="Name">"sack", or the base unit's name, "gram".</param>
/// <param name="BaseUnitsPerUnit">Base units in one: 25,000 for a 25 kg sack of flour; 1 for the base unit.</param>
/// <param name="Price">What one costs, the price the cost comes from; null with no price.</param>
public sealed record PurchaseUnit(string Name, decimal BaseUnitsPerUnit, decimal? Price)
{
    public static PurchaseUnit Of(RawMaterial material, IReadOnlyList<RawMaterialBrandPrice> brandPrices)
    {
        if (RawMaterialRules.FindCostBrandPrice([.. brandPrices.Select(p => (p.Brand, p.Price, p.Unit?.BaseUnitsPerUnit))], material.PreferredBrand) is { } dearest)
        {
            var price = brandPrices[dearest];
            return new(price.Unit?.UnitOfMeasure?.Name ?? BaseUnitText.NameOf(material), price.Unit?.BaseUnitsPerUnit ?? 1m, price.Price);
        }

        return new(BaseUnitText.NameOf(material), 1m, null);
    }
}
