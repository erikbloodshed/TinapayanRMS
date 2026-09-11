using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure;

public class SubAssemblyRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ISubAssemblyRepository
{
    public async Task<IReadOnlyList<SubAssembly>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.SubAssemblies
            .AsNoTracking()
            .Include(s => s.Role)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<SubAssembly?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.SubAssemblies
            .AsNoTracking()
            .Include(s => s.Role)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task AddAsync(SubAssembly subAssembly, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.SubAssemblies.Add(subAssembly);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SubAssembly subAssembly, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.SubAssemblies.FirstOrDefaultAsync(s => s.Id == subAssembly.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Sub-assembly {subAssembly.Id} does not exist.");

        // Field by field: TotalWeight/TotalCost are owned by the line rollup (SetLinesAsync),
        // not by this form.
        existing.Name = subAssembly.Name;
        existing.RoleId = subAssembly.RoleId;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.SubAssemblies.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
