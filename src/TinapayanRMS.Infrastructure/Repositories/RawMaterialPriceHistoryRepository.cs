using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public class RawMaterialPriceHistoryRepository(
    IDbContextFactory<TinapayanRMSDbContext> dbContextFactory,
    IngredientCostPropagator costPropagator) : IRawMaterialPriceHistoryRepository
{
    public async Task<IReadOnlyList<RawMaterialPriceHistory>> GetForRawMaterialAsync(int rawMaterialId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.RawMaterialPriceHistories
            .AsNoTracking()
            .Include(h => h.Unit)
            .ThenInclude(u => u!.UnitOfMeasure)
            .Where(h => h.RawMaterialId == rawMaterialId)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(RawMaterialPriceHistory entry, CancellationToken cancellationToken)
    {
        PriceHistoryRules.EnsureValidPrice(entry.Price);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var material = await dbContext.RawMaterials.FirstOrDefaultAsync(m => m.Id == entry.RawMaterialId, cancellationToken)
            ?? throw new InvalidOperationException($"Raw material {entry.RawMaterialId} does not exist.");

        if (material.Source != RawMaterialSource.Purchased)
        {
            throw new InvalidOperationException("Only purchased materials have price history.");
        }

        await EnsureUnitBelongsToMaterialAsync(dbContext, entry, cancellationToken);

        // Only the current latest entry can ever be displaced by the new one, so there's
        // no need to load the material's whole price history — a table that only grows
        // over the life of the app — just to find it.
        var currentLatest = await dbContext.RawMaterialPriceHistories
            .Where(h => h.RawMaterialId == entry.RawMaterialId)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        dbContext.RawMaterialPriceHistories.Add(entry);

        await ApplyLatestPriceAsync(dbContext, material, PriceHistoryRules.FindLatestAfterAdding(currentLatest, entry), cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        await PropagateAsync(material, cancellationToken);
    }

    public async Task UpdateAsync(RawMaterialPriceHistory entry, CancellationToken cancellationToken)
    {
        PriceHistoryRules.EnsureValidPrice(entry.Price);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var material = await dbContext.RawMaterials.FirstOrDefaultAsync(m => m.Id == entry.RawMaterialId, cancellationToken)
            ?? throw new InvalidOperationException($"Raw material {entry.RawMaterialId} does not exist.");

        await EnsureUnitBelongsToMaterialAsync(dbContext, entry, cancellationToken);

        // Field by field, not a blind Update(): the caller's entry may carry a Unit navigation
        // loaded for display, which must not be attached and saved along with it.
        var existing = await dbContext.RawMaterialPriceHistories
            .FirstOrDefaultAsync(h => h.Id == entry.Id && h.RawMaterialId == entry.RawMaterialId, cancellationToken)
            ?? throw new InvalidOperationException($"Price history entry {entry.Id} does not exist.");
        existing.Date = entry.Date;
        existing.UnitId = entry.UnitId;
        existing.Price = entry.Price;

        var currentLatestOther = await dbContext.RawMaterialPriceHistories
            .Where(h => h.RawMaterialId == entry.RawMaterialId && h.Id != entry.Id)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        RawMaterialPriceHistory[] candidates = currentLatestOther is null ? [existing] : [currentLatestOther, existing];
        await ApplyLatestPriceAsync(dbContext, material, PriceHistoryRules.FindLatest(candidates), cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        await PropagateAsync(material, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entry = await dbContext.RawMaterialPriceHistories.FirstOrDefaultAsync(h => h.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Price history entry {id} does not exist.");

        var material = await dbContext.RawMaterials.FirstOrDefaultAsync(m => m.Id == entry.RawMaterialId, cancellationToken)
            ?? throw new InvalidOperationException($"Raw material {entry.RawMaterialId} does not exist.");

        // The query's own ordering already picks the same "latest" row PriceHistoryRules.
        // FindLatest would from a fully-loaded list, so there's no in-memory candidate left
        // to run through it.
        var latest = await dbContext.RawMaterialPriceHistories
            .Where(h => h.RawMaterialId == entry.RawMaterialId && h.Id != id)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        dbContext.RawMaterialPriceHistories.Remove(entry);

        await ApplyLatestPriceAsync(dbContext, material, latest, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        await PropagateAsync(material, cancellationToken);
    }

    /// <summary>
    /// An ingredient's new cost ripples to any made ingredient that (directly or transitively) uses
    /// it as a BOM component, and to every sub-assembly using any of them. A packaging material's
    /// needs nothing: products cost their packaging lines each time they're read.
    /// </summary>
    private Task PropagateAsync(RawMaterial material, CancellationToken cancellationToken) =>
        material.Type == RawMaterialType.Ingredient
            ? costPropagator.RecomputeAndPropagateAsync(material.Id, cancellationToken)
            : Task.CompletedTask;

    /// <summary>A price's unit must be one of that material's own units (or none, for a price per base unit).</summary>
    private static async Task EnsureUnitBelongsToMaterialAsync(TinapayanRMSDbContext dbContext, RawMaterialPriceHistory entry, CancellationToken cancellationToken)
    {
        if (entry.UnitId is not { } unitId)
        {
            return;
        }

        var belongs = await dbContext.RawMaterialUnits.AnyAsync(u => u.Id == unitId && u.RawMaterialId == entry.RawMaterialId, cancellationToken);
        if (!belongs)
        {
            throw new InvalidOperationException("A price's unit must be one of that material's own units.");
        }
    }

    /// <summary>CostPerBaseUnit from <paramref name="latest"/>'s price and the base units in the unit it was priced in.</summary>
    private static async Task ApplyLatestPriceAsync(
        TinapayanRMSDbContext dbContext, RawMaterial material, RawMaterialPriceHistory? latest, CancellationToken cancellationToken)
    {
        decimal? baseUnitsPerUnit = latest?.UnitId is { } unitId
            ? await dbContext.RawMaterialUnits.Where(u => u.Id == unitId).Select(u => u.BaseUnitsPerUnit).SingleAsync(cancellationToken)
            : null;

        RawMaterialRules.ApplyLatestPrice(material, latest?.Price, baseUnitsPerUnit);
    }
}
