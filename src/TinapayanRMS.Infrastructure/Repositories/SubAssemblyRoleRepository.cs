using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public class SubAssemblyRoleRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ISubAssemblyRoleRepository
{
    public Task<IReadOnlyList<SubAssemblyRole>> GetAllAsync(CancellationToken cancellationToken) =>
        dbContextFactory.QueryAsync<SubAssemblyRole>(
            $"SELECT {Columns.Of<SubAssemblyRole>("r")} FROM SubAssemblyRoles r ORDER BY r.SortOrder", null, cancellationToken);

    public async Task AddAsync(SubAssemblyRole role, CancellationToken cancellationToken)
    {
        SubAssemblyRules.EnsureProductOwnedAllowed(role);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.SubAssemblyRoles.Add(role);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SubAssemblyRole role, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await dbContext.SubAssemblyRoles.FirstOrDefaultAsync(r => r.Id == role.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Sub-assembly role {role.Id} does not exist.");

        // The flag is checked against the role as stored: IsPackaging is never the caller's to change.
        SubAssemblyRules.EnsureProductOwnedAllowed(new SubAssemblyRole { Name = existing.Name, IsPackaging = existing.IsPackaging, IsProductOwned = role.IsProductOwned });

        var oldName = existing.Name;
        var wasProductOwned = existing.IsProductOwned;
        existing.Name = role.Name;
        existing.SortOrder = role.SortOrder;
        existing.IsProductOwned = role.IsProductOwned;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!wasProductOwned && role.IsProductOwned)
        {
            await SubAssemblyRepository.AdoptSingleUseAsync(dbContext, role.Id, cancellationToken);
        }
        else if (wasProductOwned && !role.IsProductOwned)
        {
            await SubAssemblyRepository.ReleaseOwnedAsync(dbContext, role.Id, cancellationToken);
        }
        else if (role.IsProductOwned && !string.Equals(oldName, role.Name, StringComparison.Ordinal))
        {
            // The ones products own in it are named after the role, "Cheese Roll Filling: Cheese": they take its new name.
            var owned = await dbContext.SubAssemblies
                .Include(s => s.OwnerProduct)
                .Where(s => s.RoleId == role.Id && s.OwnerProductId != null)
                .ToListAsync(cancellationToken);
            await SubAssemblyRepository.ResyncOwnedNamesAsync(
                dbContext,
                owned,
                s => SubAssemblyRules.OwnedNameAfterRename(s.Name, s.OwnerProduct!.Name, oldName, s.OwnerProduct.Name, role.Name),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> CountOwnershipChangesAsync(int roleId, bool makeOwned, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return makeOwned
            ? await SubAssemblyRepository.SingleUse(dbContext, roleId).CountAsync(cancellationToken)
            : await dbContext.SubAssemblies.CountAsync(s => s.RoleId == roleId && s.OwnerProductId != null, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Packaging sets live in it, and nothing else can stand in for it: renaming it is fine, deleting it isn't.
        var isPackaging = await dbContext.SubAssemblyRoles.AnyAsync(r => r.Id == id && r.IsPackaging, cancellationToken);
        if (isPackaging)
        {
            throw new InvalidOperationException("The Packaging role holds the packaging sets, so it can't be deleted. It can be renamed.");
        }

        await dbContext.SubAssemblyRoles.Where(r => r.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
