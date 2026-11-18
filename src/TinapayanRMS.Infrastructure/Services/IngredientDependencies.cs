using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// The graph an ingredient's cost depends on, as stored now, for the cycle checks (IngredientBomRules.EnsureNoCycle): each
/// made ingredient maps to its BOM components (its sub-assembly's lines and its add-ins, IngredientBoms), and each linked
/// ingredient to the one it's linked to. The edges are IngredientCostRules.Dependencies', the cascade's own, so what the
/// checks refuse and the order the cascade recomputes in can't disagree about what depends on what. Both kinds are edges
/// of the same graph, so a cycle can't close through a mix of the two (a made custard base using egg white, and egg white
/// linked to the custard base), nor through a sub-assembly (Butter Icing made from a recipe with Butter Icing in it).
/// </summary>
internal static class IngredientDependencies
{
    /// <summary>The graph as stored now on <paramref name="dbContext"/>, with every material's name for a refusal to name: two queries.</summary>
    public static async Task<IngredientDependencyGraph> LoadAsync(TinapayanRMSDbContext dbContext, CancellationToken cancellationToken)
    {
        var bomLines = await IngredientBoms.ForAsync(dbContext, null, cancellationToken);

        // What the edges and a refusal read of each material: its link, and its name.
        var materials = await dbContext.RawMaterials
            .Select(m => new RawMaterial { Id = m.Id, Name = m.Name, Source = m.Source, LinkedToId = m.LinkedToId })
            .ToListAsync(cancellationToken);

        return new IngredientDependencyGraph(
            IngredientCostRules.Dependencies(materials, bomLines),
            materials.ToDictionary(m => m.Id, m => m.Name));
    }

    /// <summary>
    /// Refuses any of <paramref name="madeIngredientIds"/> whose BOM, as stored now on <paramref name="dbContext"/> (its
    /// sub-assembly's lines and its add-ins), would close a loop: it in its own recipe, or an ingredient there that's made
    /// or cut from it. The graph is read once for them all.
    /// </summary>
    public static async Task EnsureNoCycleAsync(TinapayanRMSDbContext dbContext, IReadOnlyCollection<int> madeIngredientIds, CancellationToken cancellationToken)
    {
        if (madeIngredientIds.Count == 0)
        {
            return;
        }

        var graph = await LoadAsync(dbContext, cancellationToken);
        foreach (var id in madeIngredientIds)
        {
            graph.EnsureNoCycle(id, graph.DependenciesOf(id));
        }
    }
}

/// <summary>
/// What every ingredient's cost depends on as stored (<see cref="IngredientDependencies.LoadAsync"/>), and every material's
/// name, so a refusal names the ingredients that would close the loop.
/// </summary>
internal sealed class IngredientDependencyGraph(Dictionary<int, IReadOnlyList<int>> dependencies, Dictionary<int, string> names)
{
    /// <summary>What <paramref name="ingredientId"/>'s cost depends on as stored: its BOM components, or the one it's linked to.</summary>
    public IReadOnlyList<int> DependenciesOf(int ingredientId) => dependencies.GetValueOrDefault(ingredientId) ?? [];

    /// <summary>
    /// Refuses <paramref name="ingredientId"/>'s cost coming from <paramref name="newDependencyIds"/> (its BOM components,
    /// or the one it's to be linked to) when one of them already depends on it, however deeply. Its own edges as stored,
    /// which those replace, are left out. <paramref name="ingredientName"/> names it when the name stored isn't its own yet
    /// (a rename in the same save).
    /// </summary>
    public void EnsureNoCycle(int ingredientId, IReadOnlyCollection<int> newDependencyIds, string? ingredientName = null)
    {
        Dictionary<int, IReadOnlyList<int>> others = new(dependencies);
        others.Remove(ingredientId);
        IngredientBomRules.EnsureNoCycle(ingredientId, newDependencyIds, others, NameOf);

        string NameOf(int id) =>
            id == ingredientId && ingredientName is not null ? ingredientName
            : names.TryGetValue(id, out var name) ? name
            : $"Ingredient {id}";
    }
}
