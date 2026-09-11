using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure;

public class ProductSubAssemblyUsageRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductSubAssemblyUsageRepository
{
    public async Task<IReadOnlyList<ProductSubAssemblyUsage>> GetForProductAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.ProductSubAssemblyUsages
            .AsNoTracking()
            .Include(u => u.SubAssembly)
            .ThenInclude(s => s!.Role)
            .Where(u => u.ProductId == productId)
            .ToListAsync(cancellationToken);
    }

    public async Task SetUsagesAsync(int productId, IReadOnlyList<(int SubAssemblyId, decimal WeightPerPiece)> usages, CancellationToken cancellationToken)
    {
        ProductRules.EnsureValidUsages(usages);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var productExists = await dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!productExists)
        {
            throw new InvalidOperationException($"Product {productId} does not exist.");
        }

        var existingUsages = await dbContext.ProductSubAssemblyUsages
            .Where(u => u.ProductId == productId)
            .ToListAsync(cancellationToken);
        dbContext.ProductSubAssemblyUsages.RemoveRange(existingUsages);

        dbContext.ProductSubAssemblyUsages.AddRange(usages.Select(u => new ProductSubAssemblyUsage
        {
            ProductId = productId,
            SubAssemblyId = u.SubAssemblyId,
            WeightPerPiece = u.WeightPerPiece,
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
