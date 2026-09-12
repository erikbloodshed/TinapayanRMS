using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Repositories;

public class PackagingCategoryRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IPackagingCategoryRepository
{
    public async Task<IReadOnlyList<PackagingCategory>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.PackagingCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(PackagingCategory category, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.PackagingCategories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(PackagingCategory category, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.PackagingCategories.FirstOrDefaultAsync(c => c.Id == category.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Packaging category {category.Id} does not exist.");

        existing.Name = category.Name;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.PackagingCategories.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
