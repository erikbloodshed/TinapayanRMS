using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure;

public class TinapayanRMSDbContext : DbContext
{
    public TinapayanRMSDbContext(DbContextOptions<TinapayanRMSDbContext> options)
        : base(options)
    {
    }

    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    public DbSet<IngredientCategory> IngredientCategories => Set<IngredientCategory>();

    public DbSet<PackagingCategory> PackagingCategories => Set<PackagingCategory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TinapayanRMSDbContext).Assembly);
    }
}
