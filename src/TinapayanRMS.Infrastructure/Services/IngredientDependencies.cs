using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// The graph an ingredient's cost depends on, in the shape IngredientCostGraph and
/// IngredientBomRules.EnsureNoCycle take: each made ingredient maps to its BOM components, and each
/// linked ingredient to the one it's linked to. Both are edges of the same graph, so the cascade
/// orders them together and a cycle can't close through a mix of the two (a made custard base using
/// egg white, and egg white linked to the custard base).
/// </summary>
internal static class IngredientDependencies
{
    /// <summary>
    /// Every ingredient's direct dependencies. <paramref name="excludingIngredientId"/>'s own edges — its BOM
    /// lines, or its link — are left out, for a cycle check about to replace them.
    /// </summary>
    public static async Task<Dictionary<int, IReadOnlyList<int>>> LoadAsync(
        TinapayanRMSDbContext dbContext, int? excludingIngredientId, CancellationToken cancellationToken)
    {
        var bomEdges = await dbContext.IngredientBomLines
            .Where(l => l.IngredientId != excludingIngredientId)
            .Select(l => new { l.IngredientId, DependencyId = l.ComponentIngredientId })
            .ToListAsync(cancellationToken);

        var linkEdges = await dbContext.RawMaterials
            .Where(m => m.Source == RawMaterialSource.Linked && m.Id != excludingIngredientId)
            .Select(m => new { IngredientId = m.Id, DependencyId = m.LinkedToId!.Value })
            .ToListAsync(cancellationToken);

        return bomEdges.Concat(linkEdges)
            .GroupBy(e => e.IngredientId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(e => e.DependencyId).ToList());
    }
}
