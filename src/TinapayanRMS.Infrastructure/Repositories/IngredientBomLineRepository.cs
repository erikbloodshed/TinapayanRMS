using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public class IngredientBomLineRepository(
    IDbContextFactory<TinapayanRMSDbContext> dbContextFactory,
    IngredientCostPropagator costPropagator) : IIngredientBomLineRepository
{
    public async Task<IReadOnlyList<IngredientBomLine>> GetForIngredientAsync(int ingredientId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.IngredientBomLines
            .AsNoTracking()
            .Include(l => l.ComponentIngredient)
            .Include(l => l.Unit)
            .ThenInclude(u => u!.UnitOfMeasure)
            .Where(l => l.IngredientId == ingredientId)
            .ToListAsync(cancellationToken);
    }

    public async Task SetLinesAsync(
        int ingredientId, IReadOnlyList<(int ComponentIngredientId, int? UnitId, decimal Quantity)> lines, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // The old lines' delete and the new lines' insert commit together.
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await SetLinesAsync(dbContext, ingredientId, lines, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // Recomputes ingredientId's own CostPerBaseUnit from the lines just saved, then
        // ripples the change to anything that depends on it.
        await costPropagator.RecomputeAndPropagateAsync(ingredientId, cancellationToken);
    }

    /// <summary>
    /// Replaces the made ingredient's BOM with <paramref name="lines"/> and saves, once every line
    /// passes. Its cost is the caller's to cascade, after its transaction commits.
    /// </summary>
    internal static async Task SetLinesAsync(
        TinapayanRMSDbContext dbContext,
        int ingredientId,
        IReadOnlyList<(int ComponentIngredientId, int? UnitId, decimal Quantity)> lines,
        CancellationToken cancellationToken)
    {
        var ingredient = await dbContext.RawMaterials.FirstOrDefaultAsync(m => m.Id == ingredientId, cancellationToken)
            ?? throw new InvalidOperationException($"Ingredient {ingredientId} does not exist.");

        // Made implies the Ingredient type (RawMaterialRules.EnsureFieldsMatchType).
        if (ingredient.Source != RawMaterialSource.Made)
        {
            throw new InvalidOperationException("Only made ingredients have a BOM.");
        }

        var grams = await IngredientQuantityConverter.ToGramsAsync(dbContext, lines, cancellationToken);
        IngredientBomRules.EnsureValidLines([.. lines.Select((l, i) => (l.ComponentIngredientId, grams[i]))]);

        // ingredientId's own (about-to-be-replaced) lines are excluded, so the cycle
        // check only sees everyone else's current BOM.
        var otherBomLines = await dbContext.IngredientBomLines
            .Where(l => l.IngredientId != ingredientId)
            .ToListAsync(cancellationToken);

        var componentIdsByIngredientId = otherBomLines
            .GroupBy(l => l.IngredientId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(l => l.ComponentIngredientId).ToList());

        IngredientBomRules.EnsureNoCycle(ingredientId, lines.Select(l => l.ComponentIngredientId), componentIdsByIngredientId);

        var componentIds = lines.Select(l => l.ComponentIngredientId).ToList();
        var components = await dbContext.RawMaterials
            .Where(m => componentIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.Type })
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        foreach (var line in lines)
        {
            if (!components.TryGetValue(line.ComponentIngredientId, out var component))
            {
                throw new InvalidOperationException($"Ingredient {line.ComponentIngredientId} does not exist.");
            }

            RawMaterialRules.EnsureUsableInRecipes(component.Type, component.Name);
        }

        await dbContext.IngredientBomLines
            .Where(l => l.IngredientId == ingredientId)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.IngredientBomLines.AddRange(lines.Select((l, i) => new IngredientBomLine
        {
            IngredientId = ingredientId,
            ComponentIngredientId = l.ComponentIngredientId,
            Quantity = l.Quantity,
            UnitId = l.UnitId,
            Grams = grams[i],
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
