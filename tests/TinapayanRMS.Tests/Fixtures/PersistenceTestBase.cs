using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Tests.Fixtures;

/// <summary>
/// Base for every Respawn-backed integration test class. The DbContext/DbContextFactory
/// plumbing (and the Respawn reset before each test) is identical across every one of
/// them, so it lives here once instead of being retyped per file.
///
/// Deliberately does NOT carry [Collection(DatabaseCollection.Name)] itself — that
/// attribute must stay on each concrete test class. xUnit's collection/fixture-sharing
/// discovery is not guaranteed to honor an attribute declared only on a base class, and
/// getting that wrong would silently break the Respawn-reset isolation this whole
/// hierarchy exists to guarantee (tests could then run in parallel against the shared
/// database instead of being serialized into one collection).
/// </summary>
public abstract class PersistenceTestBase(DatabaseFixture fixture) : IAsyncLifetime
{
    protected CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => new(fixture.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    protected TinapayanRMSDbContext CreateDbContext() =>
        new(CreateDbContextOptions());

    protected IDbContextFactory<TinapayanRMSDbContext> CreateDbContextFactory() =>
        new TestDbContextFactory(CreateDbContextOptions());

    /// <summary>The ingredient cost cascade the raw material repositories hand ingredient cost changes to.</summary>
    protected IngredientCostPropagator CreateCostPropagator() =>
        new(CreateDbContextFactory(), new SubAssemblyCostPropagator(CreateDbContextFactory()));

    /// <summary>
    /// UnitOfMeasure has no migration-seeded rows (Phase: units are fully user-managed,
    /// no protected/well-known ids), so any test that needs a unit as an FK target seeds
    /// its own via this helper rather than relying on a shared baseline row.
    /// </summary>
    protected async Task<int> SeedUnitOfMeasureAsync(string name, string abbreviation)
    {
        await using var dbContext = CreateDbContext();
        var unit = new UnitOfMeasure { Name = name, Abbreviation = abbreviation };
        dbContext.UnitsOfMeasure.Add(unit);
        await dbContext.SaveChangesAsync(Cancellation);
        return unit.Id;
    }

    /// <summary>
    /// For a helper that seeds several rows in the same test and just needs "any valid
    /// unit" for each (the unit itself is never the point of the test) -- reuses the
    /// existing row by name within the test instead of colliding with the unique Name
    /// index on a second SeedUnitOfMeasureAsync("piece", ...) call.
    /// </summary>
    protected async Task<int> GetOrCreateUnitOfMeasureAsync(string name, string abbreviation)
    {
        await using var dbContext = CreateDbContext();
        var existing = await dbContext.UnitsOfMeasure.FirstOrDefaultAsync(u => u.Name == name, Cancellation);
        if (existing is not null)
        {
            return existing.Id;
        }

        var unit = new UnitOfMeasure { Name = name, Abbreviation = abbreviation };
        dbContext.UnitsOfMeasure.Add(unit);
        await dbContext.SaveChangesAsync(Cancellation);
        return unit.Id;
    }

    /// <summary>
    /// Gives an ingredient a "kilogram" alternative unit of 1,000 g and returns the unit's id, so a
    /// test can price it per kilogram (₱90 a kilogram is ₱0.09 a gram).
    /// </summary>
    protected async Task<int> AddKilogramUnitAsync(int ingredientId)
    {
        var kilogramId = await GetOrCreateUnitOfMeasureAsync("kilogram", "kg");

        await using var dbContext = CreateDbContext();
        var unit = new RawMaterialUnit { RawMaterialId = ingredientId, UnitOfMeasureId = kilogramId, BaseUnitsPerUnit = 1000m };
        dbContext.RawMaterialUnits.Add(unit);
        await dbContext.SaveChangesAsync(Cancellation);
        return unit.Id;
    }

    /// <summary>The id of the kilogram unit AddKilogramUnitAsync gave <paramref name="ingredientId"/>.</summary>
    protected async Task<int> KilogramUnitIdAsync(int ingredientId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterialUnits
            .Where(u => u.RawMaterialId == ingredientId && u.UnitOfMeasure!.Name == "kilogram")
            .Select(u => u.Id)
            .SingleAsync(Cancellation);
    }

    private DbContextOptions<TinapayanRMSDbContext> CreateDbContextOptions() =>
        new DbContextOptionsBuilder<TinapayanRMSDbContext>().UseSqlServer(fixture.ConnectionString).Options;

    private sealed class TestDbContextFactory(DbContextOptions<TinapayanRMSDbContext> options) : IDbContextFactory<TinapayanRMSDbContext>
    {
        public TinapayanRMSDbContext CreateDbContext() => new(options);
    }
}
