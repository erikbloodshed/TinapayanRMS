using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Upgrades;

namespace TinapayanRMS.Infrastructure;

public sealed class TinapayanRMSDbContext : DbContext
{
    public TinapayanRMSDbContext(DbContextOptions<TinapayanRMSDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// The clock every save stamps its times from: UpdatedAt, DeletedAt and CostUpdatedAt. The system's, unless the factory
    /// that made the context was given another (SharedTransactionDbContextFactory), as a test gives one it moves by hand.
    /// </summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>The shop's local time now, by <see cref="Clock"/>: what every saved time is (see ConfigureConventions).</summary>
    public DateTime LocalNow() => Clock.GetLocalNow().DateTime;

    /// <summary>
    /// Every string column's collation: case-insensitive but accent-sensitive, as SQL Server's default was until the move
    /// to PostgreSQL (2026-10-21). The unique name indexes rely on it (a raw material "flour" is a duplicate of "Flour"),
    /// and so does the order names sort in. PostgreSQL's own collations are case-sensitive, hence this ICU one.
    /// </summary>
    public const string CaseInsensitiveCollation = "case_insensitive";

    /// <summary>
    /// Points <paramref name="options"/> at PostgreSQL with snake_case table and column names. The app, `dotnet ef` and
    /// the tests all come through here, so none of them can disagree with the names the Dapper reads are written against.
    /// The server is PostgreSQL 18, and Npgsql 11 otherwise writes its SQL for 16. Migrating a database also makes the
    /// data upgrades it hasn't had yet (DataUpgrades).
    /// </summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        WithDataUpgrades(options.UseNpgsql(connectionString, npgsql => npgsql.SetPostgresVersion(18, 0)).UseSnakeCaseNamingConvention());

    /// <summary>
    /// The same, on <paramref name="connection"/>, which a context made with them uses but never closes: the connection a
    /// unit of work's contexts share (SharedTransactionDbContextFactory).
    /// </summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, DbConnection connection) =>
        WithDataUpgrades(options.UseNpgsql(connection, npgsql => npgsql.SetPostgresVersion(18, 0)).UseSnakeCaseNamingConvention());

    /// <summary>EF Core's seeding hook, which Migrate runs after the migrations and inside their transaction: the data upgrades.</summary>
    private static DbContextOptionsBuilder WithDataUpgrades(DbContextOptionsBuilder options) =>
        options
            .UseSeeding((context, _) => Upgrades.DataUpgrades.Run(context))
            .UseAsyncSeeding((context, _, cancellationToken) => Upgrades.DataUpgrades.RunAsync(context, cancellationToken));

    /// <summary>The options <see cref="Configure"/> builds, for a context made by hand rather than by the factory.</summary>
    public static DbContextOptions<TinapayanRMSDbContext> CreateOptions(string connectionString) =>
        (DbContextOptions<TinapayanRMSDbContext>)Configure(new DbContextOptionsBuilder<TinapayanRMSDbContext>(), connectionString).Options;

    /// <summary>The options <see cref="Configure(DbContextOptionsBuilder, DbConnection)"/> builds.</summary>
    public static DbContextOptions<TinapayanRMSDbContext> CreateOptions(DbConnection connection) =>
        (DbContextOptions<TinapayanRMSDbContext>)Configure(new DbContextOptionsBuilder<TinapayanRMSDbContext>(), connection).Options;

    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    public DbSet<RawMaterialCategory> RawMaterialCategories => Set<RawMaterialCategory>();

    public DbSet<RawMaterial> RawMaterials => Set<RawMaterial>();

    public DbSet<RawMaterialUnit> RawMaterialUnits => Set<RawMaterialUnit>();

    public DbSet<RawMaterialBrandPrice> RawMaterialBrandPrices => Set<RawMaterialBrandPrice>();

    public DbSet<RawMaterialPriceHistory> RawMaterialPriceHistories => Set<RawMaterialPriceHistory>();

    public DbSet<IngredientAddInLine> IngredientAddInLines => Set<IngredientAddInLine>();

    public DbSet<SubAssemblyRole> SubAssemblyRoles => Set<SubAssemblyRole>();

    public DbSet<SubAssembly> SubAssemblies => Set<SubAssembly>();

    public DbSet<SubAssemblyLine> SubAssemblyLines => Set<SubAssemblyLine>();

    public DbSet<SubAssemblyPackagingLine> SubAssemblyPackagingLines => Set<SubAssemblyPackagingLine>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductComponent> ProductComponents => Set<ProductComponent>();

    public DbSet<ProductSubAssemblyUsage> ProductSubAssemblyUsages => Set<ProductSubAssemblyUsage>();

    public DbSet<UsageAddInLine> UsageAddInLines => Set<UsageAddInLine>();

    public DbSet<PackagingLine> PackagingLines => Set<PackagingLine>();

    public DbSet<CostingParameters> CostingParameters => Set<CostingParameters>();

    public DbSet<CostingDefaults> CostingDefaults => Set<CostingDefaults>();

    public DbSet<PackagingLink> PackagingLinks => Set<PackagingLink>();

    public DbSet<ProductVersion> ProductVersions => Set<ProductVersion>();

    public DbSet<PriceScenario> PriceScenarios => Set<PriceScenario>();

    public DbSet<PriceScenarioLine> PriceScenarioLines => Set<PriceScenarioLine>();

    /// <summary>The data upgrades this database has had (DataUpgrades).</summary>
    public DbSet<DataUpgrade> DataUpgrades => Set<DataUpgrade>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasCollation(CaseInsensitiveCollation, locale: "und-u-ks-level2", provider: "icu", deterministic: false);
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
        var now = LocalNow();
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

    /// <summary>The decimal places every decimal column has, unless its configuration says otherwise (ConfigureConventions).</summary>
    public const int DecimalScale = 4;

    /// <summary>
    /// <paramref name="value"/> as a <see cref="DecimalScale"/>-place column stores it, rounded half away from zero as
    /// PostgreSQL rounds a numeric. For a figure worked out in memory and compared with the stored one, such as a sub-assembly's totals:
    /// at full precision they never compare equal, and every save would write them again.
    /// </summary>
    public static decimal Stored(decimal value) => decimal.Round(value, DecimalScale, MidpointRounding.AwayFromZero);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every decimal column is numeric(18,4) unless a configuration overrides it. Npgsql's default is an
        // unconstrained numeric, which would store each cost at whatever scale the division behind it left.
        configurationBuilder.Properties<decimal>().HavePrecision(18, DecimalScale);
        configurationBuilder.Properties<string>().UseCollation(CaseInsensitiveCollation);

        // The shop's local time, stamped by LocalNow(). Npgsql maps a DateTime to timestamptz by default, which only
        // takes UTC values.
        configurationBuilder.Properties<DateTime>().HaveColumnType("timestamp without time zone");
    }
}
