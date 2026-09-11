using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure;

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

        var existingEntries = await dbContext.PackagingPriceHistories
            .Where(h => h.PackagingMaterialId == entry.PackagingMaterialId)
            .ToListAsync(cancellationToken);

        dbContext.PackagingPriceHistories.Add(entry);

        var latest = PriceHistoryRules.FindLatest(existingEntries.Append(entry));
        PackagingMaterialRules.ApplyLatestPrice(packagingMaterial, latest);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(PackagingPriceHistory entry, CancellationToken cancellationToken)
    {
        PriceHistoryRules.EnsureValidPrice(entry.Price);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var packagingMaterial = await dbContext.PackagingMaterials.FirstOrDefaultAsync(p => p.Id == entry.PackagingMaterialId, cancellationToken)
            ?? throw new InvalidOperationException($"Packaging material {entry.PackagingMaterialId} does not exist.");

        var otherEntries = await dbContext.PackagingPriceHistories
            .Where(h => h.PackagingMaterialId == entry.PackagingMaterialId && h.Id != entry.Id)
            .ToListAsync(cancellationToken);

        dbContext.PackagingPriceHistories.Update(entry);

        var latest = PriceHistoryRules.FindLatest(otherEntries.Append(entry));
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

        var remainingEntries = await dbContext.PackagingPriceHistories
            .Where(h => h.PackagingMaterialId == entry.PackagingMaterialId && h.Id != id)
            .ToListAsync(cancellationToken);

        dbContext.PackagingPriceHistories.Remove(entry);

        var latest = PriceHistoryRules.FindLatest(remainingEntries);
        PackagingMaterialRules.ApplyLatestPrice(packagingMaterial, latest);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
