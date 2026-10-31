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
    public static void Map(RouteGroupBuilder api) =>
        api.MapGet("/products/recipes", async (IProductRepository products, SavedProductReader savedProducts, CancellationToken cancellationToken) =>
            (await savedProducts.ReadAllAsync(await products.GetAllAsync(cancellationToken), CostBasis.Buffered, cancellationToken))
                .Select(InfoOf)
                .ToList());

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
        saved.Costs.WastagePercent);
}
