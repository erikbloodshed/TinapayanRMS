using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore;

namespace TinapayanRMS.Infrastructure;

public class LookupRepository<T>(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ILookupRepository<T> where T : class
{
    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Set<T>().AsNoTracking().ToListAsync(cancellationToken);
    }
}
