using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// The app's DbContext factory, and its unit of work (IUnitOfWork). Outside <see cref="RunAsync"/> each context it makes
/// has a connection of its own, as AddDbContextFactory's would. Inside, every context made on that async flow, by any
/// repository, shares RunAsync's one connection and its transaction, and a repository's own transaction joins it
/// (Transactions.BeginOrJoinTransactionAsync), so the whole run commits or rolls back as one.
/// </summary>
public sealed class SharedTransactionDbContextFactory(string connectionString) : IDbContextFactory<TinapayanRMSDbContext>, IUnitOfWork
{
    /// <summary>
    /// The transaction a run on this async flow holds. Static, so a factory made apart from the one running it (each test
    /// makes its own) joins it too; but only for the same database, which the backup tests' other databases aren't.
    /// </summary>
    private static readonly AsyncLocal<SharedTransaction?> Current = new();

    private readonly DbContextOptions<TinapayanRMSDbContext> _options = TinapayanRMSDbContext.CreateOptions(connectionString);

    public TinapayanRMSDbContext CreateDbContext()
    {
        if (Shared() is not { } shared)
        {
            return new TinapayanRMSDbContext(_options);
        }

        var dbContext = new TinapayanRMSDbContext(shared.Options);
        dbContext.Database.UseTransaction(shared.Transaction);
        return dbContext;
    }

    /// <summary>
    /// Serializable, as IProductVersionRepository numbers versions: a version written in the run has to be numbered as
    /// if nothing else were writing them.
    /// </summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        if (Shared() is not null)
        {
            return await work();
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        // Set here, so it reaches the work, and gone again when this returns: an AsyncLocal set in an async method never
        // flows back out to its caller.
        Current.Value = new SharedTransaction(connectionString, transaction, TinapayanRMSDbContext.CreateOptions(connection));
        var result = await work();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private SharedTransaction? Shared() => Current.Value is { } shared && shared.ConnectionString == connectionString ? shared : null;

    private sealed record SharedTransaction(string ConnectionString, DbTransaction Transaction, DbContextOptions<TinapayanRMSDbContext> Options);
}
