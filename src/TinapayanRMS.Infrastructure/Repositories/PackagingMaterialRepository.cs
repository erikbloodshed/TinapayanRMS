using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Repositories;

public class PackagingMaterialRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IPackagingMaterialRepository
{
    public async Task<IReadOnlyList<PackagingMaterial>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.PackagingMaterials
            .AsNoTracking()
            .Include(p => p.PackagingCategory)
            .Include(p => p.BaseUnit)
            .Include(p => p.Units.OrderByDescending(u => u.BaseUnitsPerUnit))
            .ThenInclude(u => u.UnitOfMeasure)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<PackagingMaterial?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.PackagingMaterials
            .AsNoTracking()
            .Include(p => p.PackagingCategory)
            .Include(p => p.BaseUnit)
            .Include(p => p.Units.OrderByDescending(u => u.BaseUnitsPerUnit))
            .ThenInclude(u => u.UnitOfMeasure)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task AddAsync(PackagingMaterial packagingMaterial, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.PackagingMaterials.Add(packagingMaterial);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(PackagingMaterial packagingMaterial, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.PackagingMaterials.FirstOrDefaultAsync(p => p.Id == packagingMaterial.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Packaging material {packagingMaterial.Id} does not exist.");

        if (existing.BaseUnitId != packagingMaterial.BaseUnitId)
        {
            PackagingMaterialRules.EnsureBaseUnitCanChange(
                hasUnits: await dbContext.PackagingMaterialUnits.AnyAsync(u => u.PackagingMaterialId == existing.Id, cancellationToken),
                hasPrices: await dbContext.PackagingPriceHistories.AnyAsync(h => h.PackagingMaterialId == existing.Id, cancellationToken),
                hasLines: await dbContext.PackagingLines.AnyAsync(l => l.PackagingMaterialId == existing.Id, cancellationToken));
        }

        // Field by field, not a blind Update(): CostPerBaseUnit is owned by the price-history rollup
        // (PackagingMaterialRules.ApplyLatestPrice), and the units by PackagingMaterialUnitRepository.
        existing.Name = packagingMaterial.Name;
        existing.PackagingCategoryId = packagingMaterial.PackagingCategoryId;
        existing.BaseUnitId = packagingMaterial.BaseUnitId;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.PackagingMaterials.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
