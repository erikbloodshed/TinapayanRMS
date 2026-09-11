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

    public async Task AddAsync(PackagingMaterial packagingMaterial, CancellationToken cancellationToken)
    {
        PackagingMaterialRules.EnsureValidStandardUnit(packagingMaterial.StandardUnitId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.PackagingMaterials.Add(packagingMaterial);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(PackagingMaterial packagingMaterial, CancellationToken cancellationToken)
    {
        PackagingMaterialRules.EnsureValidStandardUnit(packagingMaterial.StandardUnitId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.PackagingMaterials.Update(packagingMaterial);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.PackagingMaterials.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
