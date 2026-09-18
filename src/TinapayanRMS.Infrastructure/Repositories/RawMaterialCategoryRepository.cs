using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Repositories;

public class RawMaterialCategoryRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IRawMaterialCategoryRepository
{
    public async Task<IReadOnlyList<RawMaterialCategory>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.RawMaterialCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(RawMaterialCategory category, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.RawMaterialCategories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(RawMaterialCategory category, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.RawMaterialCategories.FirstOrDefaultAsync(c => c.Id == category.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Category {category.Id} does not exist.");

        if (existing.Type != category.Type)
        {
            RawMaterialRules.EnsureCategoryTypeCanChange(
                existing.Name, await dbContext.RawMaterials.CountAsync(m => m.CategoryId == existing.Id, cancellationToken));
        }

        existing.Name = category.Name;
        existing.Type = category.Type;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.RawMaterialCategories.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
