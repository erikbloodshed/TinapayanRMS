using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
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
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.SubAssemblyRoles.Add(role);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SubAssemblyRole role, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.SubAssemblyRoles.FirstOrDefaultAsync(r => r.Id == role.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Sub-assembly role {role.Id} does not exist.");

        existing.Name = role.Name;
        existing.SortOrder = role.SortOrder;

        await dbContext.SaveChangesAsync(cancellationToken);
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
