using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure;

public class TinapayanRMSDbContext : DbContext
{
    public TinapayanRMSDbContext(DbContextOptions<TinapayanRMSDbContext> options)
        : base(options)
    {
    }

    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    public DbSet<RawMaterialCategory> RawMaterialCategories => Set<RawMaterialCategory>();

    public DbSet<RawMaterial> RawMaterials => Set<RawMaterial>();

    public DbSet<RawMaterialUnit> RawMaterialUnits => Set<RawMaterialUnit>();

    public DbSet<RawMaterialBrandPrice> RawMaterialBrandPrices => Set<RawMaterialBrandPrice>();

    public DbSet<RawMaterialPriceHistory> RawMaterialPriceHistories => Set<RawMaterialPriceHistory>();

    public DbSet<IngredientBomLine> IngredientBomLines => Set<IngredientBomLine>();

    public DbSet<SubAssemblyRole> SubAssemblyRoles => Set<SubAssemblyRole>();

    public DbSet<SubAssembly> SubAssemblies => Set<SubAssembly>();

    public DbSet<SubAssemblyLine> SubAssemblyLines => Set<SubAssemblyLine>();

    public DbSet<SubAssemblyPackagingLine> SubAssemblyPackagingLines => Set<SubAssemblyPackagingLine>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductSubAssemblyUsage> ProductSubAssemblyUsages => Set<ProductSubAssemblyUsage>();

    public DbSet<UsageAddInLine> UsageAddInLines => Set<UsageAddInLine>();

    public DbSet<PackagingLine> PackagingLines => Set<PackagingLine>();

    public DbSet<CostingParameters> CostingParameters => Set<CostingParameters>();

    public DbSet<CostingDefaults> CostingDefaults => Set<CostingDefaults>();

    public DbSet<PackagingLink> PackagingLinks => Set<PackagingLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TinapayanRMSDbContext).Assembly);
    }

    /// <summary>
    /// Stamps <see cref="RawMaterial.CostUpdatedAt"/> on every material whose stored cost this save actually
    /// changes. It lives here rather than in each repository because a cost is written from many places — a brand
    /// price, a buffer or buffered-cost edit, a BOM or weight-loss edit, a unit multiplier correction, and the
    /// cascade reaching a made or linked ingredient from a component — and a new one would be easy to forget.
    /// Comparing the original values means a save that leaves the cost alone leaves the date alone too.
    /// </summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampCostUpdatedAt();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampCostUpdatedAt();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void StampCostUpdatedAt()
    {
        var now = DateTime.Now;
        foreach (var entry in ChangeTracker.Entries<RawMaterial>())
        {
            var costChanged = entry.State switch
            {
                EntityState.Added => entry.Entity.CostPerBaseUnit != 0m || entry.Entity.BufferedCostPerBaseUnit != 0m,
                EntityState.Modified =>
                    entry.Property(m => m.CostPerBaseUnit).IsModified
                        && !Equals(entry.Property(m => m.CostPerBaseUnit).OriginalValue, entry.Entity.CostPerBaseUnit)
                    || entry.Property(m => m.BufferedCostPerBaseUnit).IsModified
                        && !Equals(entry.Property(m => m.BufferedCostPerBaseUnit).OriginalValue, entry.Entity.BufferedCostPerBaseUnit),
                _ => false,
            };

            if (costChanged)
            {
                entry.Entity.CostUpdatedAt = now;
            }
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every decimal column defaults to this precision unless a configuration overrides it,
        // so a future column can't silently fall back to SQL Server's decimal(18,2) default and
        // truncate a fractional cost (e.g. a ribbon's ₱0.045/cm).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }
}
