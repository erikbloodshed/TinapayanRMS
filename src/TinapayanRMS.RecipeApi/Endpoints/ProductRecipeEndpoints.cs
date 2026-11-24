using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.RecipeApi.Contracts;

namespace TinapayanRMS.RecipeApi.Endpoints;

/// <summary>
/// Every product's recipe, which TinapayanBMS plans production from, and every
/// raw material, which its purchase requisition prices. The products are read as saved (SavedProductReader.ReadAllAsync),
/// at buffered costs, each with its packaging per unit, Wastage % and costing.
/// </summary>
public static class ProductRecipeEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/products/recipes", async (IProductRepository products, SavedProductReader savedProducts, CancellationToken cancellationToken) =>
            (await savedProducts.ReadAllAsync(await products.GetAllAsync(cancellationToken), CostBasis.Buffered, cancellationToken))
                .Select(InfoOf)
                .ToList());

        // Every raw material, packaging included, as TinapayanBMS's purchase requisition prices it (its
        // PlanSheetBuilder.RequisitionRowOf): the base unit, the buffered cost, and the purchase unit of its cost brand.
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

                // A locked section has no pieces of its own, whatever piece size it keeps for when it's unlocked.
                s.IsLocked ? 0m : s.AuthoredValue,
                [.. s.Lines.Select(l => new PlanLine(l.Ingredient.Id, l.Ingredient.Name, l.Grams))],
                [.. s.AddIns.Select(a => new PlanLine(a.Ingredient.Id, a.Ingredient.Name, a.Grams))],
                SectionLines: [.. s.SectionLines.Select(l => new SectionLineInfo(l.SubAssemblyId, l.SectionName, l.Grams))],
                IsLocked: s.IsLocked)),
        ],
        saved.Costs.WastagePercent,
        PackagingPerUnit(saved),
        saved.Product.IncludesComponentPackaging,
        new ProductCostingInfo(saved.Costs.CurrentPrice, saved.Costs.SellingPrice, saved.Costs.VatPercent, saved.Costs.BaseCost));

    /// <summary>
    /// What one sellable unit takes of each packaging material, in its base unit: its packaging lines, and its share of the
    /// bulk pack's (each line's quantity for the pack over how many products it holds), one entry a material.
    /// </summary>
    private static List<PackagingPerUnitInfo> PackagingPerUnit(SavedProduct saved)
    {
        var bulk = saved.BulkPackaging is { Quantity: >= 1 } pack
            ? pack.Lines.Select(l => (l.PackagingMaterialId, BaseQty: l.BaseQty / pack.Quantity!.Value))
            : [];
        return
        [
            .. saved.PackagingLines.Select(l => (l.PackagingMaterialId, l.BaseQty))
                .Concat(bulk)
                .Where(l => l.PackagingMaterialId is not null && l.BaseQty is > 0m)
                .GroupBy(l => l.PackagingMaterialId!.Value)
                .Select(g => new PackagingPerUnitInfo(g.Key, g.Sum(l => l.BaseQty!.Value))),
        ];
    }
}
