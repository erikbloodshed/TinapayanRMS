using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Repositories;

public class PackagingPriceHistoryRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IPackagingPriceHistoryRepository
{
    public async Task<IReadOnlyList<PackagingPriceHistory>> GetForPackagingMaterialAsync(int packagingMaterialId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.PackagingPriceHistories
            .AsNoTracking()
            .Include(h => h.PackagingMaterialUnit)
            .ThenInclude(u => u!.UnitOfMeasure)
            .Where(h => h.PackagingMaterialId == packagingMaterialId)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(PackagingPriceHistory entry, CancellationToken cancellationToken)
    {
        PriceHistoryRules.EnsureValidPrice(entry.Price);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var packagingMaterial = await dbContext.PackagingMaterials.FirstOrDefaultAsync(p => p.Id == entry.PackagingMaterialId, cancellationToken)
            ?? throw new InvalidOperationException($"Packaging material {entry.PackagingMaterialId} does not exist.");

        await EnsureUnitBelongsToMaterialAsync(dbContext, entry, cancellationToken);

        // Only the current latest entry can ever be displaced by the new one, so there's
        // no need to load the material's whole price history — a table that only grows
        // over the life of the app — just to find it.
        var currentLatest = await dbContext.PackagingPriceHistories
            .Where(h => h.PackagingMaterialId == entry.PackagingMaterialId)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        dbContext.PackagingPriceHistories.Add(entry);

        await ApplyLatestPriceAsync(dbContext, packagingMaterial, PriceHistoryRules.FindLatestAfterAdding(currentLatest, entry), cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(PackagingPriceHistory entry, CancellationToken cancellationToken)
    {
        PriceHistoryRules.EnsureValidPrice(entry.Price);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var packagingMaterial = await dbContext.PackagingMaterials.FirstOrDefaultAsync(p => p.Id == entry.PackagingMaterialId, cancellationToken)
            ?? throw new InvalidOperationException($"Packaging material {entry.PackagingMaterialId} does not exist.");

        await EnsureUnitBelongsToMaterialAsync(dbContext, entry, cancellationToken);

        // Field by field, not a blind Update(): the caller's entry may carry a PackagingMaterialUnit
        // navigation loaded for display, which must not be attached and saved along with it.
        var existing = await dbContext.PackagingPriceHistories
            .FirstOrDefaultAsync(h => h.Id == entry.Id && h.PackagingMaterialId == entry.PackagingMaterialId, cancellationToken)
            ?? throw new InvalidOperationException($"Price history entry {entry.Id} does not exist.");
        existing.Date = entry.Date;
        existing.PackagingMaterialUnitId = entry.PackagingMaterialUnitId;
        existing.Price = entry.Price;

        var currentLatestOther = await dbContext.PackagingPriceHistories
            .Where(h => h.PackagingMaterialId == entry.PackagingMaterialId && h.Id != entry.Id)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        PackagingPriceHistory[] candidates = currentLatestOther is null ? [existing] : [currentLatestOther, existing];
        await ApplyLatestPriceAsync(dbContext, packagingMaterial, PriceHistoryRules.FindLatest(candidates), cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entry = await dbContext.PackagingPriceHistories.FirstOrDefaultAsync(h => h.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Price history entry {id} does not exist.");

        var packagingMaterial = await dbContext.PackagingMaterials.FirstOrDefaultAsync(p => p.Id == entry.PackagingMaterialId, cancellationToken)
            ?? throw new InvalidOperationException($"Packaging material {entry.PackagingMaterialId} does not exist.");

        // The query's own ordering already picks the same "latest" row PriceHistoryRules.
        // FindLatest would from a fully-loaded list, so there's no in-memory candidate left
        // to run through it.
        var latest = await dbContext.PackagingPriceHistories
            .Where(h => h.PackagingMaterialId == entry.PackagingMaterialId && h.Id != id)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        dbContext.PackagingPriceHistories.Remove(entry);

        await ApplyLatestPriceAsync(dbContext, packagingMaterial, latest, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A price's unit must be one of that material's own units (or none, for a price per base unit).</summary>
    private static async Task EnsureUnitBelongsToMaterialAsync(TinapayanRMSDbContext dbContext, PackagingPriceHistory entry, CancellationToken cancellationToken)
    {
        if (entry.PackagingMaterialUnitId is not { } unitId)
        {
            return;
        }

        var belongs = await dbContext.PackagingMaterialUnits.AnyAsync(u => u.Id == unitId && u.PackagingMaterialId == entry.PackagingMaterialId, cancellationToken);
        if (!belongs)
        {
            throw new InvalidOperationException("A price's unit must be one of that material's own units.");
        }
    }

    /// <summary>CostPerBaseUnit from <paramref name="latest"/>'s price and the base units in the unit it was priced in.</summary>
    private static async Task ApplyLatestPriceAsync(
        TinapayanRMSDbContext dbContext, PackagingMaterial packagingMaterial, PackagingPriceHistory? latest, CancellationToken cancellationToken)
    {
        decimal? baseUnitsPerUnit = latest?.PackagingMaterialUnitId is { } unitId
            ? await dbContext.PackagingMaterialUnits.Where(u => u.Id == unitId).Select(u => u.BaseUnitsPerUnit).SingleAsync(cancellationToken)
            : null;

        PackagingMaterialRules.ApplyLatestPrice(packagingMaterial, latest?.Price, baseUnitsPerUnit);
    }
}
