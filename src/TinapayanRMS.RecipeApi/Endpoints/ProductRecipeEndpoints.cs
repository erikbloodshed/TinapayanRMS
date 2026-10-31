using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.RecipeApi.Contracts;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.RecipeApi.Endpoints;

/// <summary>
/// Every product's recipe as the Production Plan reads it (2026-10-31): what TinapayanPreweigh plans from now that planning
/// moves there. The products are read as saved, as /plan reads them (SavedProductReader.ReadAllAsync), and given in the
/// terms ProductionPlanBuilder.RecipeOf hands ProductionPlanRules, packaging left out.
/// </summary>
public static class ProductRecipeEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/products/recipes", async (IProductRepository products, SavedProductReader savedProducts, CancellationToken cancellationToken) =>
            (await savedProducts.ReadAllAsync(await products.GetAllAsync(cancellationToken), CostBasis.Buffered, cancellationToken))
                .Select(InfoOf)
                .ToList());

        // Every raw material, packaging included, as the Production Plan's purchase requisition prices it (its
        // RequisitionRowOf): the base unit, the buffered cost, and the purchase unit of its cost brand.
        api.MapGet("/materials", async (IRawMaterialRepository materials, IRawMaterialBrandPriceRepository brandPrices, CancellationToken cancellationToken) =>
        {
            var prices = (await brandPrices.GetAllAsync(cancellationToken)).ToLookup(p => p.RawMaterialId);
            return (await materials.GetAllAsync(cancellationToken))
                .Select(m =>
                {
                    var unit = PurchaseUnit.Of(m, [.. prices[m.Id]]);
                    return new MaterialInfo(
                        m.Id,
                        m.Name,
                        m.Type.ToString(),
                        BaseUnitText.AbbreviationOf(m),
                        m.BufferedCostPerBaseUnit,
                        unit.Price is null ? null : unit.Name,
                        unit.BaseUnitsPerUnit,
                        unit.Price);
                })
                .ToList();
        });
    }

    private static ProductRecipeInfo InfoOf(SavedProduct saved) => new(
        saved.Product.Id,
        saved.Product.QuantityRequired,
        [.. saved.Components.Select(c => new ComponentInfo(c.Component.ComponentProductId, c.Component.Quantity))],
        [
            .. saved.Sections.Select(s => new SectionInfo(
                s.SubAssemblyId,
                s.SectionName,
                s.LinkedName,
                s.RoleName,
                s.AuthoredField.ToString(),
                s.AuthoredValue,
                [.. s.Lines.Select(l => new PlanLine(l.Ingredient.Id, l.Ingredient.Name, l.Grams))],
                [.. s.AddIns.Select(a => new PlanLine(a.Ingredient.Id, a.Ingredient.Name, a.Grams))])),
        ],
        saved.Costs.WastagePercent,
        [.. saved.PackagingLines.Where(l => l.PackagingMaterialId is not null && l.BaseQty is > 0m).Select(l => new PackagingPerUnitInfo(l.PackagingMaterialId!.Value, l.BaseQty!.Value))],
        saved.Product.IncludesComponentPackaging,
        new ProductCostingInfo(saved.Costs.CurrentPrice, saved.Costs.SellingPrice, saved.Costs.VatPercent, saved.Costs.BaseCost));
}
