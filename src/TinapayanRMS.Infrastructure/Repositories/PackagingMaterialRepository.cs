using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure;

public class PackagingMaterialRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IPackagingMaterialRepository
{
    public async Task<IReadOnlyList<PackagingMaterial>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.PackagingMaterials
            .AsNoTracking()
            .Include(p => p.PackagingCategory)
            .Include(p => p.StandardUnit)
            .Include(p => p.PurchaseUnit)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<PackagingMaterial?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.PackagingMaterials
            .AsNoTracking()
            .Include(p => p.PackagingCategory)
            .Include(p => p.StandardUnit)
            .Include(p => p.PurchaseUnit)
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

        // Field by field, not a blind Update(): CurrentCost is owned by the price-history
        // rollup (PackagingMaterialRules.ApplyLatestPrice), not by this form.
        existing.Name = packagingMaterial.Name;
        existing.PackagingCategoryId = packagingMaterial.PackagingCategoryId;
        existing.StandardUnitId = packagingMaterial.StandardUnitId;
        existing.PurchaseUnitId = packagingMaterial.PurchaseUnitId;
        existing.PurchaseUnitQty = packagingMaterial.PurchaseUnitQty;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.PackagingMaterials.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
