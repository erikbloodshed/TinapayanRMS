using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Repositories;

public class SubAssemblyRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ISubAssemblyRepository
{
    public async Task<IReadOnlyList<SubAssembly>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.SubAssemblies
            .AsNoTracking()
            .Include(s => s.Role)
            .Include(s => s.OwnerProduct)
            .Where(s => ids.Contains(s.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubAssembly>> GetLinkableAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.SubAssemblies
            .AsNoTracking()
            .Include(s => s.Role)
            .Include(s => s.OwnerProduct)
            .Where(s => s.TotalWeight > 0)
            .OrderBy(s => s.Name)
            .ThenBy(s => s.OwnerProduct!.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetLinkedByAsync(int ownerProductId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await GetLinkedByAsync(dbContext, u => u.SubAssembly!.OwnerProductId == ownerProductId, cancellationToken);
    }

    /// <summary>
    /// For each sub-assembly matching <paramref name="subAssemblyFilter"/> (applied to its usages) that a
    /// product other than its owner uses, the names of those products. One query.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetLinkedByAsync(
        TinapayanRMSDbContext dbContext,
        System.Linq.Expressions.Expression<Func<ProductSubAssemblyUsage, bool>> subAssemblyFilter,
        CancellationToken cancellationToken)
    {
        var links = await dbContext.ProductSubAssemblyUsages
            .Where(subAssemblyFilter)
            .Where(u => u.ProductId != u.SubAssembly!.OwnerProductId)
            .Select(u => new { u.SubAssemblyId, ProductName = u.Product!.Name })
            .ToListAsync(cancellationToken);

        return links
            .GroupBy(l => l.SubAssemblyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(l => l.ProductName).Order(StringComparer.CurrentCultureIgnoreCase)]);
    }

    /// <summary>
    /// Refuses to go on while another product links a sub-assembly matching
    /// <paramref name="subAssemblyFilter"/>, naming the first such sub-assembly and every product linking
    /// it (SubAssemblyRules.EnsureNotLinkedElsewhere).
    /// </summary>
    internal static async Task EnsureNoneLinkedElsewhereAsync(
        TinapayanRMSDbContext dbContext,
        System.Linq.Expressions.Expression<Func<ProductSubAssemblyUsage, bool>> subAssemblyFilter,
        CancellationToken cancellationToken)
    {
        var linkedBy = await GetLinkedByAsync(dbContext, subAssemblyFilter, cancellationToken);
        if (linkedBy.Count == 0)
        {
            return;
        }

        var (subAssemblyId, productNames) = linkedBy.First();
        var name = await dbContext.SubAssemblies.Where(s => s.Id == subAssemblyId).Select(s => s.Name).FirstAsync(cancellationToken);
        SubAssemblyRules.EnsureNotLinkedElsewhere(name, productNames);
    }
}
