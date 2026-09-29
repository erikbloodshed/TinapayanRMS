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

        // A composite's own sections are only in product-owned roles, so while one has a section in this role it stays one.
        if (existing.IsProductOwned && !role.IsProductOwned
            && await ProductComponentRepository.CompositesUsingRoleAsync(dbContext, existing.Id, null, cancellationToken) is [_, ..] composites)
        {
            throw new InvalidOperationException(
                $"{ProductComponentRepository.NamesHave(composites)} a {existing.Name} section, and a composite product can only have "
                + $"sections in product-owned roles. Take it out there first, then untick Product-owned.");
        }

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

        if (!string.Equals(oldName, role.Name, StringComparison.Ordinal))
        {
            // The ones products own in it are named after the role, "Cheese Roll Filling: Cheese": they take its new name.
            // Whether or not it's product-owned, since a product can unlink a section into its own in any role.
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
