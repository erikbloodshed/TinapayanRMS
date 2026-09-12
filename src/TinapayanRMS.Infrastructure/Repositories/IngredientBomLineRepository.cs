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
            .Where(l => l.IngredientId == ingredientId)
            .ToListAsync(cancellationToken);
    }

    public async Task SetLinesAsync(int ingredientId, IReadOnlyList<(int ComponentIngredientId, decimal Grams)> lines, CancellationToken cancellationToken)
    {
        IngredientBomRules.EnsureValidLines(lines);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ingredient = await dbContext.Ingredients.FirstOrDefaultAsync(i => i.Id == ingredientId, cancellationToken)
            ?? throw new InvalidOperationException($"Ingredient {ingredientId} does not exist.");

        if (ingredient.Source != IngredientSource.Made)
        {
            throw new InvalidOperationException("Only made ingredients have a BOM.");
        }

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

        dbContext.IngredientBomLines.AddRange(lines.Select(l => new IngredientBomLine
        {
            IngredientId = ingredientId,
            ComponentIngredientId = l.ComponentIngredientId,
            Grams = l.Grams,
        }));

        await dbContext.SaveChangesAsync(cancellationToken);

        // Recomputes ingredientId's own CostPerGram from the lines just saved, then
        // ripples the change to anything that depends on it.
        await costPropagator.RecomputeAndPropagateAsync(ingredientId, cancellationToken);
    }
}
