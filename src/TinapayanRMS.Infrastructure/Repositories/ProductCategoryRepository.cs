using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public sealed class ProductCategoryRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductCategoryRepository
{
    public Task<IReadOnlyList<ProductCategory>> GetAllAsync(CancellationToken cancellationToken) =>
        dbContextFactory.QueryAsync<ProductCategory>($"SELECT {Columns.Of<ProductCategory>("c")} FROM product_categories c ORDER BY c.name", null, cancellationToken);

    public async Task AddAsync(ProductCategory category, CancellationToken cancellationToken)
    {
        EnsureValidFigures(category);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.ProductCategories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ProductCategory category, CancellationToken cancellationToken)
    {
        EnsureValidFigures(category);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.ProductCategories.FirstOrDefaultAsync(c => c.Id == category.Id, cancellationToken)
            ?? throw new BusinessRuleException($"Category {category.Id} does not exist.");

        existing.Name = category.Name;
        existing.WastagePercent = category.WastagePercent;
        existing.LaborPercent = category.LaborPercent;
        existing.OverheadPercent = category.OverheadPercent;
        existing.AdministrativePercent = category.AdministrativePercent;
        existing.MarkupPercent = category.MarkupPercent;
        existing.MarkupFloorPercent = category.MarkupFloorPercent;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.ProductCategories.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Its own wastage, costing and Warn Below Markup percentages, each checked as Settings' are; a blank one is Settings'.</summary>
    private static void EnsureValidFigures(ProductCategory category)
    {
        if (category.WastagePercent is { } wastage)
        {
            CostingRules.EnsureValidWastage(wastage);
        }

        CostingRules.EnsureValidCategoryCosting(category);
        CostingRules.EnsureValidMarkupFloor(category.MarkupFloorPercent);
    }
}
