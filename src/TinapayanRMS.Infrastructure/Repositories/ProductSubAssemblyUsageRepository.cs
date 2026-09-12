using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Repositories;

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

    public async Task<IReadOnlyList<ProductSubAssemblyUsage>> GetEffectiveForRoleAsync(int productId, int subAssemblyRoleId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await ResolveEffectiveForRoleAsync(dbContext, productId, subAssemblyRoleId, [], cancellationToken);
    }

    public async Task<IReadOnlyList<ProductSubAssemblyUsage>> GetEffectiveForProductAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var roleIds = await dbContext.SubAssemblyRoles.Select(r => r.Id).ToListAsync(cancellationToken);

        var result = new List<ProductSubAssemblyUsage>();
        foreach (var roleId in roleIds)
        {
            result.AddRange(await ResolveEffectiveForRoleAsync(dbContext, productId, roleId, [], cancellationToken));
        }

        return result;
    }

    /// <summary>Follows a role's tab link chain (if any) until an unlinked tab is found, then returns that tab's own rows. visitedProductIds guards against a cycle slipping past SetLinkAsync's own check (e.g. a manual data edit).</summary>
    private static async Task<IReadOnlyList<ProductSubAssemblyUsage>> ResolveEffectiveForRoleAsync(
        TinapayanRMSDbContext dbContext, int productId, int subAssemblyRoleId, HashSet<int> visitedProductIds, CancellationToken cancellationToken)
    {
        if (!visitedProductIds.Add(productId))
        {
            throw new InvalidOperationException("Circular product tab link detected.");
        }

        var link = await dbContext.ProductTabLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.ProductId == productId && l.TabKind == ProductTabKind.SubAssemblyRole && l.SubAssemblyRoleId == subAssemblyRoleId, cancellationToken);

        if (link is not null)
        {
            return await ResolveEffectiveForRoleAsync(dbContext, link.LinkedProductId, subAssemblyRoleId, visitedProductIds, cancellationToken);
        }

        return await dbContext.ProductSubAssemblyUsages
            .AsNoTracking()
            .Include(u => u.SubAssembly)
            .ThenInclude(s => s!.Role)
            .Where(u => u.ProductId == productId && u.SubAssembly!.RoleId == subAssemblyRoleId)
            .ToListAsync(cancellationToken);
    }

    public async Task SetUsagesForRoleAsync(int productId, int subAssemblyRoleId, IReadOnlyList<(int SubAssemblyId, decimal WeightPerPiece)> usages, CancellationToken cancellationToken)
    {
        ProductRules.EnsureValidUsages(usages);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var productExists = await dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!productExists)
        {
            throw new InvalidOperationException($"Product {productId} does not exist.");
        }

        var tabIsLinked = await dbContext.ProductTabLinks
            .AnyAsync(l => l.ProductId == productId && l.TabKind == ProductTabKind.SubAssemblyRole && l.SubAssemblyRoleId == subAssemblyRoleId, cancellationToken);
        if (tabIsLinked)
        {
            throw new InvalidOperationException("This tab is Live Template-linked — unlink it before editing its usages directly.");
        }

        var providedIds = usages.Select(u => u.SubAssemblyId).ToList();
        var actualRoleIdBySubAssemblyId = await dbContext.SubAssemblies
            .Where(s => providedIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.RoleId, cancellationToken);

        foreach (var id in providedIds)
        {
            if (!actualRoleIdBySubAssemblyId.TryGetValue(id, out var actualRoleId))
            {
                throw new InvalidOperationException($"Sub-assembly {id} does not exist.");
            }

            if (actualRoleId != subAssemblyRoleId)
            {
                throw new InvalidOperationException($"Sub-assembly {id} doesn't belong to this tab's role.");
            }
        }

        var subAssemblyIdsInThisRole = dbContext.SubAssemblies.Where(s => s.RoleId == subAssemblyRoleId).Select(s => s.Id);
        await dbContext.ProductSubAssemblyUsages
            .Where(u => u.ProductId == productId && subAssemblyIdsInThisRole.Contains(u.SubAssemblyId))
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.ProductSubAssemblyUsages.AddRange(usages.Select(u => new ProductSubAssemblyUsage
        {
            ProductId = productId,
            SubAssemblyId = u.SubAssemblyId,
            WeightPerPiece = u.WeightPerPiece,
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
