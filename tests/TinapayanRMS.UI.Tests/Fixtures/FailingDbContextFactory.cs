using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Infrastructure;

namespace TinapayanRMS.UI.Tests.Fixtures;

/// <summary>
/// The test's own factory, which refuses every context while <see cref="Fail"/> is set, as a database that stops
/// answering does: for a screen's reads and saves to fail part-way through a test, and work again after. While
/// <see cref="Hold"/> is set, a context asked for asynchronously waits for it first, as a slow database keeps a read on its
/// way.
/// </summary>
public sealed class FailingDbContextFactory(IDbContextFactory<TinapayanRMSDbContext> inner) : IDbContextFactory<TinapayanRMSDbContext>
{
    public const string Message = "Simulated: the database didn't answer in time.";

    private volatile bool _fail;
    private volatile TaskCompletionSource? _hold;

    public bool Fail
    {
        get => _fail;
        set => _fail = value;
    }

    /// <summary>Set to keep every read asked for from then on waiting until it's completed.</summary>
    public TaskCompletionSource? Hold
    {
        get => _hold;
        set => _hold = value;
    }

    public TinapayanRMSDbContext CreateDbContext() => _fail ? throw new TimeoutException(Message) : inner.CreateDbContext();

    public async Task<TinapayanRMSDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        if (_hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        return _fail ? throw new TimeoutException(Message) : await inner.CreateDbContextAsync(cancellationToken);
    }
}
