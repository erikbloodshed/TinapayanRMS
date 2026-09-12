using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure;

public class IngredientPriceHistoryRepository(
    IDbContextFactory<TinapayanRMSDbContext> dbContextFactory,
    IngredientCostPropagator costPropagator) : IIngredientPriceHistoryRepository
{
    public async Task<IReadOnlyList<IngredientPriceHistory>> GetForIngredientAsync(int ingredientId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.IngredientPriceHistories
            .AsNoTracking()
            .Where(h => h.IngredientId == ingredientId)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(IngredientPriceHistory entry, CancellationToken cancellationToken)
    {
        PriceHistoryRules.EnsureValidPrice(entry.Price);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ingredient = await dbContext.Ingredients.FirstOrDefaultAsync(i => i.Id == entry.IngredientId, cancellationToken)
            ?? throw new InvalidOperationException($"Ingredient {entry.IngredientId} does not exist.");

        if (ingredient.Source != IngredientSource.Purchased)
        {
            throw new InvalidOperationException("Only purchased ingredients have price history.");
        }

        // Only the current latest entry can ever be displaced by the new one, so there's
        // no need to load the ingredient's whole price history — a table that only grows
        // over the life of the app — just to find it.
        var currentLatest = await dbContext.IngredientPriceHistories
            .Where(h => h.IngredientId == entry.IngredientId)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        dbContext.IngredientPriceHistories.Add(entry);

        IngredientPriceHistory[] candidates = currentLatest is null ? [entry] : [currentLatest, entry];
        var latest = PriceHistoryRules.FindLatest(candidates);
        IngredientRules.ApplyLatestPrice(ingredient, latest);

        await dbContext.SaveChangesAsync(cancellationToken);

        // This ingredient's own cost just changed — ripple it to any made ingredient
        // that (directly or transitively) uses it as a BOM component.
        await costPropagator.RecomputeAndPropagateAsync(ingredient.Id, cancellationToken);
    }

    public async Task UpdateAsync(IngredientPriceHistory entry, CancellationToken cancellationToken)
    {
        PriceHistoryRules.EnsureValidPrice(entry.Price);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ingredient = await dbContext.Ingredients.FirstOrDefaultAsync(i => i.Id == entry.IngredientId, cancellationToken)
            ?? throw new InvalidOperationException($"Ingredient {entry.IngredientId} does not exist.");

        var currentLatestOther = await dbContext.IngredientPriceHistories
            .Where(h => h.IngredientId == entry.IngredientId && h.Id != entry.Id)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        dbContext.IngredientPriceHistories.Update(entry);

        IngredientPriceHistory[] candidates = currentLatestOther is null ? [entry] : [currentLatestOther, entry];
        var latest = PriceHistoryRules.FindLatest(candidates);
        IngredientRules.ApplyLatestPrice(ingredient, latest);

        await dbContext.SaveChangesAsync(cancellationToken);

        await costPropagator.RecomputeAndPropagateAsync(ingredient.Id, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entry = await dbContext.IngredientPriceHistories.FirstOrDefaultAsync(h => h.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Price history entry {id} does not exist.");

        var ingredient = await dbContext.Ingredients.FirstOrDefaultAsync(i => i.Id == entry.IngredientId, cancellationToken)
            ?? throw new InvalidOperationException($"Ingredient {entry.IngredientId} does not exist.");

        // The query's own ordering already picks the same "latest" row PriceHistoryRules.
        // FindLatest would from a fully-loaded list, so there's no in-memory candidate left
        // to run through it.
        var latest = await dbContext.IngredientPriceHistories
            .Where(h => h.IngredientId == entry.IngredientId && h.Id != id)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        dbContext.IngredientPriceHistories.Remove(entry);

        IngredientRules.ApplyLatestPrice(ingredient, latest);

        await dbContext.SaveChangesAsync(cancellationToken);

        await costPropagator.RecomputeAndPropagateAsync(ingredient.Id, cancellationToken);
    }
}
