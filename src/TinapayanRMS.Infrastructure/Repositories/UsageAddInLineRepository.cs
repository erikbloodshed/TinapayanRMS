using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Repositories;

/// <summary>
/// Writes a linked usage's add-ins. There's no read side here: they're loaded with the product's usages
/// (<see cref="ProductSubAssemblyUsageRepository.GetAddInsForProductAsync"/>), as a usage's own detail.
/// </summary>
public static class UsageAddInLineRepository
{
    /// <summary>
    /// Replaces a linked usage's add-in lines on the caller's context, which should hold a transaction.
    /// Lines are in grams, every one must be an ingredient, and none may be one the linked recipe already
    /// has (<see cref="ProductRules.EnsureValidAddIns"/>). Nothing is deleted until every line passes.
    /// <para>
    /// Unlike <see cref="SubAssemblyLineRepository.SetLinesAsync"/> it computes no totals: an add-in is
    /// this product's own, so the sub-assembly's stored TotalWeight and TotalCost must not move. A usage's
    /// effective batch is worked out on read instead (ProductRules.CalculateEffectiveBatch).
    /// </para>
    /// </summary>
    /// <param name="usage">The usage, tracked by <paramref name="dbContext"/> and saved already (it has its id).</param>
    internal static async Task SetLinesAsync(
        TinapayanRMSDbContext dbContext,
        ProductSubAssemblyUsage usage,
        string subAssemblyName,
        IReadOnlyList<(int IngredientId, decimal Grams)> lines,
        CancellationToken cancellationToken)
    {
        var ingredientIds = lines.Select(l => l.IngredientId).ToList();
        var ingredients = await dbContext.RawMaterials
            .Where(m => ingredientIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.Type })
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        foreach (var line in lines)
        {
            if (!ingredients.TryGetValue(line.IngredientId, out var ingredient))
            {
                throw new InvalidOperationException($"Ingredient {line.IngredientId} does not exist.");
            }

            RawMaterialRules.EnsureUsableInRecipes(ingredient.Type, ingredient.Name);
        }

        var baseIngredientIds = await dbContext.SubAssemblyLines
            .Where(l => l.SubAssemblyId == usage.SubAssemblyId)
            .Select(l => l.IngredientId)
            .ToListAsync(cancellationToken);

        ProductRules.EnsureValidAddIns(
            [.. lines.Select(l => (l.IngredientId, l.Grams, ingredients[l.IngredientId].Name))],
            subAssemblyName,
            baseIngredientIds);

        await dbContext.UsageAddInLines
            .Where(l => l.UsageId == usage.Id)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.UsageAddInLines.AddRange(lines.Select(l => new UsageAddInLine
        {
            UsageId = usage.Id,
            IngredientId = l.IngredientId,
            Grams = l.Grams,
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
