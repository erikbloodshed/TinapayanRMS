using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public class IngredientPriceHistoryRepository(
    IDbContextFactory<TinapayanRMSDbContext> dbContextFactory,
    IngredientCostPropagator costPropagator) : IIngredientPriceHistoryRepository
{
    public async Task<IReadOnlyList<IngredientPriceHistory>> GetForIngredientAsync(int ingredientId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.IngredientPriceHistories
            .AsNoTracking()
            .Include(h => h.IngredientUnit)
            .ThenInclude(u => u!.UnitOfMeasure)
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

        await EnsureUnitBelongsToIngredientAsync(dbContext, entry, cancellationToken);

        // Only the current latest entry can ever be displaced by the new one, so there's
        // no need to load the ingredient's whole price history — a table that only grows
        // over the life of the app — just to find it.
        var currentLatest = await dbContext.IngredientPriceHistories
            .Where(h => h.IngredientId == entry.IngredientId)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        dbContext.IngredientPriceHistories.Add(entry);

        await ApplyLatestPriceAsync(dbContext, ingredient, PriceHistoryRules.FindLatestAfterAdding(currentLatest, entry), cancellationToken);

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

        await EnsureUnitBelongsToIngredientAsync(dbContext, entry, cancellationToken);

        // Field by field, not a blind Update(): the caller's entry may carry an IngredientUnit
        // navigation loaded for display, which must not be attached and saved along with it.
        var existing = await dbContext.IngredientPriceHistories
            .FirstOrDefaultAsync(h => h.Id == entry.Id && h.IngredientId == entry.IngredientId, cancellationToken)
            ?? throw new InvalidOperationException($"Price history entry {entry.Id} does not exist.");
        existing.Date = entry.Date;
        existing.IngredientUnitId = entry.IngredientUnitId;
        existing.Price = entry.Price;

        var currentLatestOther = await dbContext.IngredientPriceHistories
            .Where(h => h.IngredientId == entry.IngredientId && h.Id != entry.Id)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        IngredientPriceHistory[] candidates = currentLatestOther is null ? [existing] : [currentLatestOther, existing];
        await ApplyLatestPriceAsync(dbContext, ingredient, PriceHistoryRules.FindLatest(candidates), cancellationToken);

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

        await ApplyLatestPriceAsync(dbContext, ingredient, latest, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        await costPropagator.RecomputeAndPropagateAsync(ingredient.Id, cancellationToken);
    }

    /// <summary>A price's unit must be one of that ingredient's own units (or none, for a price per gram).</summary>
    private static async Task EnsureUnitBelongsToIngredientAsync(TinapayanRMSDbContext dbContext, IngredientPriceHistory entry, CancellationToken cancellationToken)
    {
        if (entry.IngredientUnitId is not { } unitId)
        {
            return;
        }

        var belongs = await dbContext.IngredientUnits.AnyAsync(u => u.Id == unitId && u.IngredientId == entry.IngredientId, cancellationToken);
        if (!belongs)
        {
            throw new InvalidOperationException("A price's unit must be one of that ingredient's own units.");
        }
    }

    /// <summary>CostPerGram from <paramref name="latest"/>'s price and the grams in the unit it was priced in.</summary>
    private static async Task ApplyLatestPriceAsync(
        TinapayanRMSDbContext dbContext, Ingredient ingredient, IngredientPriceHistory? latest, CancellationToken cancellationToken)
    {
        decimal? gramsPerUnit = latest?.IngredientUnitId is { } unitId
            ? await dbContext.IngredientUnits.Where(u => u.Id == unitId).Select(u => u.GramsPerUnit).SingleAsync(cancellationToken)
            : null;

        IngredientRules.ApplyLatestPrice(ingredient, latest?.Price, gramsPerUnit);
    }
}
