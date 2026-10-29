using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public sealed class RawMaterialCategoryRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IRawMaterialCategoryRepository
{
    // Type is stored by name; Dapper parses it back into the enum.
    public Task<IReadOnlyList<RawMaterialCategory>> GetAllAsync(CancellationToken cancellationToken) =>
        dbContextFactory.QueryAsync<RawMaterialCategory>(
            $"SELECT {Columns.Of<RawMaterialCategory>("c")} FROM raw_material_categories c ORDER BY c.name", null, cancellationToken);

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
            var materials = await dbContext.RawMaterials
                .Where(m => m.CategoryId == existing.Id)
                .Select(m => new { m.Type, m.Source })
                .ToListAsync(cancellationToken);
            RawMaterialRules.EnsureCategoryTypeCanChange(
                existing.Name, category.Type, materials.Count(m => RawMaterialRules.CategoryTypeFor(m.Type, m.Source) != category.Type));
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
