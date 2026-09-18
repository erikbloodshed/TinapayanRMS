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

    public DbSet<ProductPack> ProductPacks => Set<ProductPack>();

    public DbSet<ProductSubAssemblyUsage> ProductSubAssemblyUsages => Set<ProductSubAssemblyUsage>();

    public DbSet<UsageAddInLine> UsageAddInLines => Set<UsageAddInLine>();

    public DbSet<PackagingLine> PackagingLines => Set<PackagingLine>();

    public DbSet<CostingParameters> CostingParameters => Set<CostingParameters>();

    public DbSet<PackagingLink> PackagingLinks => Set<PackagingLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TinapayanRMSDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every decimal column defaults to this precision unless a configuration overrides it,
        // so a future column can't silently fall back to SQL Server's decimal(18,2) default and
        // truncate a fractional cost (e.g. a ribbon's ₱0.045/cm).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }
}
