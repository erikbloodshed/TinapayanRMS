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
        api.MapGet("/mixes", async (ISubAssemblyRepository subAssemblies, ISubAssemblyLineRepository lines, CancellationToken cancellationToken) =>
            (await DetailsAsync(subAssemblies, lines, cancellationToken)).Select(d => d.Mix).ToList());

        // Every mix with its lines at once (2026-10-31): what TinapayanBMS's sync copies, in a few queries rather than a
        // call per mix.
        api.MapGet("/mixes/details", (ISubAssemblyRepository subAssemblies, ISubAssemblyLineRepository lines, CancellationToken cancellationToken) =>
            DetailsAsync(subAssemblies, lines, cancellationToken));

        api.MapGet("/mixes/{id:int}",async (int id, ISubAssemblyRepository subAssemblies, ISubAssemblyLineRepository lines, CancellationToken cancellationToken) =>
        {
            var mix = (await subAssemblies.GetByIdsAsync([id], cancellationToken)).SingleOrDefault();
            if (mix?.Role is null or { IsPackaging: true })
            {
                return Results.NotFound(new ApiError($"No mix has id {id}."));
            }

            return Results.Ok(DetailOf(mix, await lines.GetForSubAssembliesAsync([id], cancellationToken)));
        });
    }

    private static async Task<List<MixDetail>> DetailsAsync(
        ISubAssemblyRepository subAssemblies, ISubAssemblyLineRepository lines, CancellationToken cancellationToken)
    {
        var mixes = await subAssemblies.GetLinkableAsync(cancellationToken);
        var linesOf = (await lines.GetForSubAssembliesAsync([.. mixes.Select(m => m.Id)], cancellationToken)).ToLookup(l => l.SubAssemblyId);
        return [.. mixes.Select(mix => DetailOf(mix, [.. linesOf[mix.Id]]))];
    }

    /// <summary>A mix with its lines in the recipe's order, grams per batch; what a batch weighs is what they weigh.</summary>
    private static MixDetail DetailOf(SubAssembly mix, IReadOnlyCollection<SubAssemblyLine> lines) => new(
        new MixInfo(mix.Id, mix.Name, mix.RoleId, mix.Role?.Name ?? string.Empty, mix.Role?.SortOrder ?? 0, lines.Sum(l => l.Grams), mix.OwnerProduct?.Name),
        [
            .. lines
                .OrderBy(l => l.SortOrder)
                .ThenBy(l => l.Id)
                .Select(l => new MixLine(l.IngredientId, l.Ingredient?.Name ?? string.Empty, l.Grams, l.IsFormulaBase)),
        ]);
}
