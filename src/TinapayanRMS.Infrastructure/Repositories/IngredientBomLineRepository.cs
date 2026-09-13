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
            .Include(l => l.IngredientUnit)
            .ThenInclude(u => u!.UnitOfMeasure)
            .Where(l => l.IngredientId == ingredientId)
            .ToListAsync(cancellationToken);
    }

    public async Task SetLinesAsync(
        int ingredientId, IReadOnlyList<(int ComponentIngredientId, int? IngredientUnitId, decimal Quantity)> lines, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ingredient = await dbContext.Ingredients.FirstOrDefaultAsync(i => i.Id == ingredientId, cancellationToken)
            ?? throw new InvalidOperationException($"Ingredient {ingredientId} does not exist.");

        if (ingredient.Source != IngredientSource.Made)
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

        var existingComponentIds = await dbContext.Ingredients
            .Where(i => lines.Select(l => l.ComponentIngredientId).Contains(i.Id))
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
        var existingComponentIdSet = existingComponentIds.ToHashSet();

        foreach (var line in lines)
        {
            if (!existingComponentIdSet.Contains(line.ComponentIngredientId))
            {
                throw new InvalidOperationException($"Ingredient {line.ComponentIngredientId} does not exist.");
            }
        }

        await dbContext.IngredientBomLines
            .Where(l => l.IngredientId == ingredientId)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.IngredientBomLines.AddRange(lines.Select((l, i) => new IngredientBomLine
        {
            IngredientId = ingredientId,
            ComponentIngredientId = l.ComponentIngredientId,
            Quantity = l.Quantity,
            IngredientUnitId = l.IngredientUnitId,
            Grams = grams[i],
        }));

        await dbContext.SaveChangesAsync(cancellationToken);

        // Recomputes ingredientId's own CostPerGram from the lines just saved, then
        // ripples the change to anything that depends on it.
        await costPropagator.RecomputeAndPropagateAsync(ingredientId, cancellationToken);
    }
}
