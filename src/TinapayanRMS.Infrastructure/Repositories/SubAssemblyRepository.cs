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
            .OrderBy(s => s.OwnerProductId != null)
            .ThenBy(s => s.Name)
            .ThenBy(s => s.OwnerProduct!.Name)
            .ThenBy(s => s.Role!.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetLinkedByAsync(int ownerProductId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await GetLinkedByAsync(dbContext, u => u.SubAssembly!.OwnerProductId == ownerProductId, cancellationToken);
    }

    public async Task<IReadOnlyList<SubAssembly>> GetStandaloneAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.SubAssemblies
            .AsNoTracking()
            .Include(s => s.Role)
            .Where(s => s.OwnerProductId == null)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetStandaloneLinkedByAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await GetLinkedByAsync(dbContext, u => u.SubAssembly!.OwnerProductId == null, cancellationToken);
    }

    public async Task<int> SaveStandaloneAsync(SubAssembly subAssembly, IReadOnlyList<RecipeLine> lines, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var name = subAssembly.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Every sub-assembly needs a name.");
        }

        var roleExists = await dbContext.SubAssemblyRoles.AnyAsync(r => r.Id == subAssembly.RoleId, cancellationToken);
        if (!roleExists)
        {
            throw new InvalidOperationException("Every sub-assembly needs a role, and that role must still exist.");
        }

        var otherNames = await dbContext.SubAssemblies
            .Where(s => s.OwnerProductId == null && s.Id != subAssembly.Id)
            .Select(s => s.Name ?? string.Empty)
            .ToListAsync(cancellationToken);
        SubAssemblyRules.EnsureStandaloneNameAvailable(name, otherNames);

        // The caller's entity is never attached, so a rolled-back save leaves no id on it.
        SubAssembly stored;
        if (subAssembly.Id == 0)
        {
            stored = new SubAssembly { Name = name, RoleId = subAssembly.RoleId };
            dbContext.SubAssemblies.Add(stored);
        }
        else
        {
            stored = await dbContext.SubAssemblies.FirstOrDefaultAsync(s => s.Id == subAssembly.Id, cancellationToken)
                ?? throw new InvalidOperationException($"Sub-assembly {subAssembly.Id} does not exist.");
            if (stored.OwnerProductId is not null)
            {
                throw new InvalidOperationException("That sub-assembly belongs to a product. Change it in that product's recipe.");
            }

            // Every product using it links it, and takes its new role with it.
            if (stored.RoleId != subAssembly.RoleId)
            {
                await EnsureRoleFreeWhereLinkedAsync(dbContext, stored.Id, subAssembly.RoleId, cancellationToken);
            }

            stored.Name = name;
            stored.RoleId = subAssembly.RoleId;
        }

        // Saved here so a new one has its id for its lines; SetLinesAsync checks them all before replacing any.
        SubAssemblyRules.EnsureValidLines(lines);
        await dbContext.SaveChangesAsync(cancellationToken);
        await SubAssemblyLineRepository.SetLinesAsync(dbContext, stored, lines, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return stored.Id;
    }

    public async Task DeleteStandaloneAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ownerProductId = await dbContext.SubAssemblies
            .Where(s => s.Id == id)
            .Select(s => new { s.OwnerProductId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Sub-assembly {id} does not exist.");
        if (ownerProductId.OwnerProductId is not null)
        {
            throw new InvalidOperationException("That sub-assembly belongs to a product. Remove it from that product's recipe instead.");
        }

        // Every product using a standalone sub-assembly links it.
        await EnsureNoneLinkedElsewhereAsync(dbContext, u => u.SubAssemblyId == id, cancellationToken);

        // Its lines go with it (Cascade).
        await dbContext.SubAssemblies.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
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
        var subAssembly = await dbContext.SubAssemblies
            .AsNoTracking()
            .Include(s => s.Role)
            .Include(s => s.OwnerProduct)
            .FirstAsync(s => s.Id == subAssemblyId, cancellationToken);
        SubAssemblyRules.EnsureNotLinkedElsewhere(SubAssemblyRules.DisplayName(subAssembly), productNames);
    }

    /// <summary>
    /// Refuses <paramref name="roleId"/> as the new role of a sub-assembly other products link, when one of them
    /// already has a sub-assembly in it: a product has one in each role (SubAssemblyRules.EnsureOnePerRole), and
    /// the link would give it a second.
    /// </summary>
    internal static async Task EnsureRoleFreeWhereLinkedAsync(TinapayanRMSDbContext dbContext, int subAssemblyId, int roleId, CancellationToken cancellationToken)
    {
        var clash = await dbContext.ProductSubAssemblyUsages
            .Where(u => u.SubAssemblyId == subAssemblyId && u.ProductId != u.SubAssembly!.OwnerProductId)
            .SelectMany(link => dbContext.ProductSubAssemblyUsages.Where(other =>
                other.ProductId == link.ProductId && other.SubAssemblyId != subAssemblyId && other.SubAssembly!.RoleId == roleId))
            .Select(other => new { ProductName = other.Product!.Name, RoleName = other.SubAssembly!.Role!.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (clash is not null)
        {
            throw new InvalidOperationException(
                $"{clash.ProductName} links this sub-assembly and already has one in the {clash.RoleName} role, and a product takes only one in each role. Change or take out {clash.ProductName}'s {clash.RoleName} first, or keep this role.");
        }
    }
}
