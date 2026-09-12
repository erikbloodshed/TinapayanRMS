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

        // Only the current latest entry can ever be displaced by the new one, so there's
        // no need to load the material's whole price history — a table that only grows
        // over the life of the app — just to find it.
        var currentLatest = await dbContext.PackagingPriceHistories
            .Where(h => h.PackagingMaterialId == entry.PackagingMaterialId)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        dbContext.PackagingPriceHistories.Add(entry);

        PackagingPriceHistory[] candidates = currentLatest is null ? [entry] : [currentLatest, entry];
        var latest = PriceHistoryRules.FindLatest(candidates);
        PackagingMaterialRules.ApplyLatestPrice(packagingMaterial, latest);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(PackagingPriceHistory entry, CancellationToken cancellationToken)
    {
        PriceHistoryRules.EnsureValidPrice(entry.Price);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var packagingMaterial = await dbContext.PackagingMaterials.FirstOrDefaultAsync(p => p.Id == entry.PackagingMaterialId, cancellationToken)
            ?? throw new InvalidOperationException($"Packaging material {entry.PackagingMaterialId} does not exist.");

        var currentLatestOther = await dbContext.PackagingPriceHistories
            .Where(h => h.PackagingMaterialId == entry.PackagingMaterialId && h.Id != entry.Id)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        dbContext.PackagingPriceHistories.Update(entry);

        PackagingPriceHistory[] candidates = currentLatestOther is null ? [entry] : [currentLatestOther, entry];
        var latest = PriceHistoryRules.FindLatest(candidates);
        PackagingMaterialRules.ApplyLatestPrice(packagingMaterial, latest);

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

        PackagingMaterialRules.ApplyLatestPrice(packagingMaterial, latest);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
