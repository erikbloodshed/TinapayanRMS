using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Infrastructure;

namespace TinapayanRMS.UI.Tests.Fixtures;

/// <summary>
/// The test's own factory, which refuses every context while <see cref="Fail"/> is set, as a database that stops
/// answering does: for a screen's reads and saves to fail part-way through a test, and work again after.
/// </summary>
public sealed class FailingDbContextFactory(IDbContextFactory<TinapayanRMSDbContext> inner) : IDbContextFactory<TinapayanRMSDbContext>
{
    public const string Message = "Simulated: the database didn't answer in time.";

    private volatile bool _fail;

    public bool Fail
    {
        get => _fail;
        set => _fail = value;
    }

    public TinapayanRMSDbContext CreateDbContext() => _fail ? throw new TimeoutException(Message) : inner.CreateDbContext();

    public Task<TinapayanRMSDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        _fail ? throw new TimeoutException(Message) : inner.CreateDbContextAsync(cancellationToken);
}
