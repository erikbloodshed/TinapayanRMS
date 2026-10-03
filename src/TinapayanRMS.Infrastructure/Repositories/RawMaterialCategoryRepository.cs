using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public class RawMaterialCategoryRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IRawMaterialCategoryRepository
{
    // Type is stored by name; Dapper parses it back into the enum.
    public Task<IReadOnlyList<RawMaterialCategory>> GetAllAsync(CancellationToken cancellationToken) =>
        dbContextFactory.QueryAsync<RawMaterialCategory>(
            $"SELECT {Columns.Of<RawMaterialCategory>("c")} FROM RawMaterialCategories c ORDER BY c.Name", null, cancellationToken);

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
            ?? throw new BusinessRuleException($"Category {category.Id} does not exist.");

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
