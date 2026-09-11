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

    public DbSet<Ingredient> Ingredients => Set<Ingredient>();

    public DbSet<PackagingMaterial> PackagingMaterials => Set<PackagingMaterial>();

    public DbSet<IngredientPriceHistory> IngredientPriceHistories => Set<IngredientPriceHistory>();

    public DbSet<PackagingPriceHistory> PackagingPriceHistories => Set<PackagingPriceHistory>();

    public DbSet<IngredientBomLine> IngredientBomLines => Set<IngredientBomLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TinapayanRMSDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every decimal column defaults to this precision unless a configuration overrides it,
        // so a future column can't silently fall back to SQL Server's decimal(18,2) default and
        // truncate a fractional cost (e.g. PackagingMaterial's ₱0.045/cm).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }
}
