using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// The graph an ingredient's cost depends on, in the shape IngredientCostGraph and
/// IngredientBomRules.EnsureNoCycle take: each made ingredient maps to its BOM components (its sub-assembly's lines and its
/// add-ins, IngredientBoms), and each linked ingredient to the one it's linked to. Both are edges of the same graph, so the
/// cascade orders them together and a cycle can't close through a mix of the two (a made custard base using egg white, and
/// egg white linked to the custard base), nor through a sub-assembly (Butter Icing made from a recipe with Butter Icing in it).
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
        var bomEdges = await IngredientBoms.Query(dbContext)
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

    /// <summary>
    /// Refuses a made ingredient whose BOM, as stored now on <paramref name="dbContext"/> (its sub-assembly's lines and its
    /// add-ins), would close a loop: it in its own recipe, or an ingredient there that's made or cut from it.
    /// </summary>
    public static async Task EnsureNoCycleAsync(TinapayanRMSDbContext dbContext, int madeIngredientId, CancellationToken cancellationToken)
    {
        var componentIds = await IngredientBoms.Query(dbContext)
            .Where(l => l.IngredientId == madeIngredientId)
            .Select(l => l.ComponentIngredientId)
            .ToListAsync(cancellationToken);
        var dependencies = await LoadAsync(dbContext, madeIngredientId, cancellationToken);
        IngredientBomRules.EnsureNoCycle(madeIngredientId, componentIds, dependencies);
    }
}
