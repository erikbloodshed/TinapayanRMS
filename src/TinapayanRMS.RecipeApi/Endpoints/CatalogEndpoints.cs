using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.RecipeApi.Contracts;

namespace TinapayanRMS.RecipeApi.Endpoints;

/// <summary>What another app looks things up in: the API itself, the ingredients, their categories and BOMs, the products and the saved plans.</summary>
public static class CatalogEndpoints
{
    public const int ApiVersion = 1;

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/status", () => new ApiStatus("TinapayanRMS", ApiVersion));

        api.MapGet("/ingredients", async (IRawMaterialRepository materials, CancellationToken cancellationToken) =>
            (await materials.GetAllAsync(cancellationToken))
                .Where(m => m.Type == RawMaterialType.Ingredient)
                .Select(m => new IngredientInfo(
                    m.Id, m.Name, m.CategoryId, m.Category?.Name ?? string.Empty, m.Source.ToString(), m.WeightLossPercent, m.LinkedToId, m.WeightSharePercent, m.SubAssemblyId))
                .ToList());

        // Each made ingredient's lines in its BOM's order, which is how the Production Plan opens them (ProductionPlanSource.From).
        api.MapGet("/boms", async (IIngredientBomLineRepository bomLines, CancellationToken cancellationToken) =>
            (await bomLines.GetAllAsync(cancellationToken))
                .Select(l => new BomLineInfo(l.IngredientId, l.ComponentIngredientId, l.Grams, l.IsAddIn))
                .ToList());

        api.MapGet("/categories", async (IRawMaterialCategoryRepository categories, CancellationToken cancellationToken) =>
            (await categories.GetAllAsync(cancellationToken))
                .Where(c => c.Type != RawMaterialCategoryType.Packaging)
                .Select(c => new CategoryInfo(c.Id, c.Name, c.Type.ToString()))
                .ToList());

        api.MapGet("/products", async (string? search, IProductRepository products, CancellationToken cancellationToken) =>
            (await products.GetAllAsync(cancellationToken))
                .Where(p => string.IsNullOrWhiteSpace(search) || p.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(p => new ProductInfo(p.Id, p.Name, p.Category?.Name, p.Stage.ToString(), p.IsComposite, p.QuantityRequired))
                .ToList());

        // A plan's lines for products in use only: one in the Deleted folder is left out, as the screen leaves it out on opening.
        api.MapGet("/plans", async (ISavedPlanRepository plans, IProductRepository products, CancellationToken cancellationToken) =>
        {
            var names = (await products.GetAllAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
            return (await plans.GetAllAsync(cancellationToken))
                .Select(plan => new SavedPlanInfo(
                    plan.Id,
                    plan.Name,
                    plan.SavedAt,
                    plan.MakeOn,
                    plan.HalfBatches,
                    [.. plan.Lines.Where(l => names.ContainsKey(l.ProductId)).Select(l => new PlanOrder(l.ProductId, l.Units, names[l.ProductId]))]))
                .ToList();
        });
    }
}
