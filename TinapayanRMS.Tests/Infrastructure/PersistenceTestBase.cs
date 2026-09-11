using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Infrastructure;

namespace TinapayanRMS.Tests.Infrastructure;

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

    private DbContextOptions<TinapayanRMSDbContext> CreateDbContextOptions() =>
        new DbContextOptionsBuilder<TinapayanRMSDbContext>().UseSqlServer(fixture.ConnectionString).Options;

    private sealed class TestDbContextFactory(DbContextOptions<TinapayanRMSDbContext> options) : IDbContextFactory<TinapayanRMSDbContext>
    {
        public TinapayanRMSDbContext CreateDbContext() => new(options);
    }
}
