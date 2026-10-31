using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.RecipeApi.Contracts;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.RecipeApi.Endpoints;

/// <summary>
/// A production plan worked out for the products asked for, as the Production Plan screen works it out
/// (ProductionPlanRules.Plan over the products as saved), with its mixes named as the Mixing Sheet names them. Nothing is
/// saved: it's a POST only because the orders go in its body.
/// </summary>
public static class PlanEndpoints
{
    public static void Map(RouteGroupBuilder api) => api.MapPost("/plan", PlanAsync);

    private static async Task<IResult> PlanAsync(
        PlanRequest request,
        IProductRepository productRepository,
        IRawMaterialRepository rawMaterialRepository,
        IIngredientBomLineRepository bomLineRepository,
        SavedProductReader savedProducts,
        CancellationToken cancellationToken)
    {
        if (request.Orders.Count == 0)
        {
            return Results.BadRequest(new ApiError("Give at least one product to make."));
        }

        if (request.Orders.FirstOrDefault(o => o.Units <= 0m) is { } none)
        {
            return Results.BadRequest(new ApiError(string.Create(CultureInfo.InvariantCulture, $"Product {none.ProductId}: its units must be above 0.")));
        }

        // What the Production Plan screen reads (ProductionPlanViewModel.ReadAsync), but brand prices: nothing here is costed.
        var products = await productRepository.GetAllAsync(cancellationToken);
        if (request.Orders.FirstOrDefault(o => products.All(p => p.Id != o.ProductId)) is { } unknown)
        {
            return Results.NotFound(new ApiError(string.Create(CultureInfo.InvariantCulture, $"No product in use has id {unknown.ProductId}.")));
        }

        var saved = (await savedProducts.ReadAllAsync(products, CostBasis.Buffered, cancellationToken)).ToDictionary(s => s.Product.Id);
        var materials = (await rawMaterialRepository.GetAllAsync(cancellationToken)).ToDictionary(m => m.Id);
        var source = ProductionPlanSource.From(
            saved, materials, await bomLineRepository.GetAllAsync(cancellationToken), Array.Empty<RawMaterialBrandPrice>().ToLookup(p => p.RawMaterialId));
        var plan = ProductionPlanRules.Plan(
            [.. request.Orders.Select(o => (o.ProductId, o.Units))], source.Recipes, source.Ingredients, source.Boms, request.HalfBatches);

        string NameOf(int id) => materials.GetValueOrDefault(id)?.Name ?? $"Raw material {id}";
        List<PlanLine> LinesOf(IEnumerable<(int IngredientId, decimal Grams)> lines) =>
            [.. lines.Select(l => new PlanLine(l.IngredientId, NameOf(l.IngredientId), l.Grams))];

        List<PlannedMixInfo> mixes =
        [
            .. plan.Mixes
                .Select(mix => new PlannedMixInfo(
                    mix.SubAssemblyId,
                    ProductionPlanBuilder.MixName(mix, saved, NameOf),
                    saved[mix.Uses[0].ProductId].Sections.First(s => s.SubAssemblyId == mix.SubAssemblyId).RoleName,
                    ProductionPlanBuilder.ForText(mix.Uses, saved),
                    mix.Batches,
                    mix.BatchWeight,
                    LinesOf(mix.Lines)))
                .OrderBy(mix => mix.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
        List<PreparedInfo> prepared =
        [
            .. plan.Prepared
                .Select(p => new PreparedInfo(
                    p.Key, materials.GetValueOrDefault(p.Key)?.SubAssemblyId, NameOf(p.Key), p.Value, LinesOf(plan.PreparedLines.GetValueOrDefault(p.Key, []))))
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
        return Results.Ok(new PlanResult(mixes, prepared, [.. plan.Issues.Select(issue => ProductionPlanBuilder.NoteOf(issue, saved, NameOf)).Distinct()]));
    }
}
