using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Repositories;

/// <summary>
/// The log of prices a material has been costed at. Entries are added by RawMaterialBrandPriceRepository
/// when a material's cost moves; deleting one only tidies the log, and never reprices anything.
/// </summary>
public class RawMaterialPriceHistoryRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IRawMaterialPriceHistoryRepository
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

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await dbContext.RawMaterialPriceHistories.Where(h => h.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            throw new InvalidOperationException($"Price history entry {id} does not exist.");
        }
    }
}
