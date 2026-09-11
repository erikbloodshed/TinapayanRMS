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

        var existingEntries = await dbContext.IngredientPriceHistories
            .Where(h => h.IngredientId == entry.IngredientId)
            .ToListAsync(cancellationToken);

        dbContext.IngredientPriceHistories.Add(entry);

        var latest = PriceHistoryRules.FindLatest(existingEntries.Append(entry));
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

        var otherEntries = await dbContext.IngredientPriceHistories
            .Where(h => h.IngredientId == entry.IngredientId && h.Id != entry.Id)
            .ToListAsync(cancellationToken);

        dbContext.IngredientPriceHistories.Update(entry);

        var latest = PriceHistoryRules.FindLatest(otherEntries.Append(entry));
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

        var remainingEntries = await dbContext.IngredientPriceHistories
            .Where(h => h.IngredientId == entry.IngredientId && h.Id != id)
            .ToListAsync(cancellationToken);

        dbContext.IngredientPriceHistories.Remove(entry);

        var latest = PriceHistoryRules.FindLatest(remainingEntries);
        IngredientRules.ApplyLatestPrice(ingredient, latest);

        await dbContext.SaveChangesAsync(cancellationToken);

        await costPropagator.RecomputeAndPropagateAsync(ingredient.Id, cancellationToken);
    }
}
