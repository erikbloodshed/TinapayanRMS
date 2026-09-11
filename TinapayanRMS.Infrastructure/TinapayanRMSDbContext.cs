using Microsoft.EntityFrameworkCore;

namespace TinapayanRMS.Infrastructure;

public class TinapayanRMSDbContext : DbContext
{
    public TinapayanRMSDbContext(DbContextOptions<TinapayanRMSDbContext> options)
        : base(options)
    {
    }
}
