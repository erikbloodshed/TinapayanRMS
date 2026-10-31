using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.RecipeApi.Contracts;

namespace TinapayanRMS.RecipeApi.Endpoints;

/// <summary>The mixes, the sub-assemblies with ingredients, and what goes into a batch of one.</summary>
public static class MixEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        // What products can link or copy: every sub-assembly with lines, a product's own ones included, never a packaging set.
        api.MapGet("/mixes", async (ISubAssemblyRepository subAssemblies, CancellationToken cancellationToken) =>
            (await subAssemblies.GetLinkableAsync(cancellationToken)).Select(InfoOf).ToList());

        // Every mix with its lines at once (2026-10-31): what TinapayanPreweigh's sync copies, in a few queries rather than a
        // call per mix.
        api.MapGet("/mixes/details", async (ISubAssemblyRepository subAssemblies, ISubAssemblyLineRepository lines, CancellationToken cancellationToken) =>
        {
            var mixes = await subAssemblies.GetLinkableAsync(cancellationToken);
            var linesOf = (await lines.GetForSubAssembliesAsync([.. mixes.Select(m => m.Id)], cancellationToken)).ToLookup(l => l.SubAssemblyId);
            return mixes.Select(mix => new MixDetail(InfoOf(mix), LinesOf(linesOf[mix.Id]))).ToList();
        });

        api.MapGet("/mixes/{id:int}",async (int id, ISubAssemblyRepository subAssemblies, ISubAssemblyLineRepository lines, CancellationToken cancellationToken) =>
        {
            var mix = (await subAssemblies.GetByIdsAsync([id], cancellationToken)).SingleOrDefault();
            if (mix?.Role is null or { IsPackaging: true })
            {
                return Results.NotFound(new ApiError($"No mix has id {id}."));
            }

            return Results.Ok(new MixDetail(InfoOf(mix), LinesOf(await lines.GetForSubAssembliesAsync([id], cancellationToken))));
        });
    }

    /// <summary>A mix's lines in the recipe's order, grams per batch.</summary>
    private static List<MixLine> LinesOf(IEnumerable<SubAssemblyLine> lines) =>
    [
        .. lines
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Id)
            .Select(l => new MixLine(l.IngredientId, l.Ingredient?.Name ?? string.Empty, l.Grams, l.IsFormulaBase)),
    ];

    private static MixInfo InfoOf(SubAssembly s) =>
        new(s.Id, s.Name, s.RoleId, s.Role?.Name ?? string.Empty, s.Role?.SortOrder ?? 0, s.TotalWeight, s.OwnerProduct?.Name);
}
