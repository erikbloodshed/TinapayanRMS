using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Repositories;

public class ProductRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductRepository
{
    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Products
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await AddAsync(dbContext, product, cancellationToken);
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await UpdateAsync(dbContext, product, cancellationToken);
    }

    public async Task<int> SaveAsync(
        Product product,
        IReadOnlyList<(int SubAssemblyRoleId, IReadOnlyList<(int SubAssemblyId, decimal WeightPerPiece)> Usages)> changedRoles,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)>? packagingLines,
        CostingParameters? costing,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // One transaction for every part, so a part that's rejected leaves nothing half-saved. Each
        // step saves as it goes, so the checks of the steps after it see what it wrote (a new product's
        // id, above all).
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // A copy of the form's fields: the caller's entity is never attached, and never given an id by
        // a save that then rolls back.
        var fields = new Product { Id = product.Id, Name = product.Name, Description = product.Description };
        if (fields.Id == 0)
        {
            await AddAsync(dbContext, fields, cancellationToken);
        }
        else
        {
            await UpdateAsync(dbContext, fields, cancellationToken);
        }

        foreach (var (roleId, usages) in changedRoles)
        {
            await ProductSubAssemblyUsageRepository.SetUsagesForRoleAsync(dbContext, fields.Id, roleId, usages, cancellationToken);
        }

        if (packagingLines is not null)
        {
            await PackagingLineRepository.SetLinesAsync(dbContext, fields.Id, packagingLines, cancellationToken);
        }

        if (costing is not null)
        {
            await CostingParametersRepository.SetAsync(
                dbContext,
                new CostingParameters
                {
                    ProductId = fields.Id,
                    LaborCost = costing.LaborCost,
                    OverheadCost = costing.OverheadCost,
                    AdministrativeCost = costing.AdministrativeCost,
                    MarginPercent = costing.MarginPercent,
                    VatPercent = costing.VatPercent,
                },
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return fields.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Products.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Adds <paramref name="product"/>, stamped as saved now, and saves it: the product gets its id.</summary>
    internal static async Task AddAsync(TinapayanRMSDbContext dbContext, Product product, CancellationToken cancellationToken)
    {
        product.UpdatedAt = DateTime.Now;
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Copies the name and description onto the stored product, stamps it as saved now, and saves it.</summary>
    internal static async Task UpdateAsync(TinapayanRMSDbContext dbContext, Product product, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == product.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Product {product.Id} does not exist.");

        existing.Name = product.Name;
        existing.Description = product.Description;
        existing.UpdatedAt = DateTime.Now;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
