using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.RecipeApi.Contracts;

namespace TinapayanRMS.RecipeApi.Endpoints;

/// <summary>What another app looks things up in: the API itself, the ingredients, their categories and BOMs, and the products.</summary>
public static class CatalogEndpoints
{
    public const int ApiVersion = 1;

    public static void Map(RouteGroupBuilder api)
    {
        // Reads something, so an answer means the database is there as well as this server: when it can't be read, this
        // answers as any other request then does (RecipeApiHost).
        api.MapGet("/status", async (ICostingDefaultsRepository costingDefaults, CancellationToken cancellationToken) =>
        {
            await costingDefaults.GetAsync(cancellationToken);
            return new ApiStatus("TinapayanRMS", ApiVersion);
        });

        api.MapGet("/ingredients", async (IRawMaterialRepository materials, CancellationToken cancellationToken) =>
            (await materials.GetAllAsync(cancellationToken))
                .Where(m => m.Type == RawMaterialType.Ingredient)
                .Select(m => new IngredientInfo(
                    m.Id, m.Name, m.CategoryId, m.Category?.Name ?? string.Empty, m.Source.ToString(), m.WeightLossPercent, m.LinkedToId, m.WeightSharePercent, m.SubAssemblyId))
                .ToList());

        // Each made ingredient's lines in its BOM's order, which is how TinapayanBMS's planning opens them.
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
    }
}
