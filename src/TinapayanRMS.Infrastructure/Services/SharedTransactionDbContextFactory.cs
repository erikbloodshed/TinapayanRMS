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
/// <param name="clock">The clock each context stamps saved times from (TinapayanRMSDbContext.Clock): the system's by default.</param>
public sealed class SharedTransactionDbContextFactory(string connectionString, TimeProvider? clock = null) : IDbContextFactory<TinapayanRMSDbContext>, IUnitOfWork
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

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
            return new TinapayanRMSDbContext(_options) { Clock = _clock };
        }

        var dbContext = new TinapayanRMSDbContext(shared.Options) { Clock = _clock };
        dbContext.Database.UseTransaction(shared.Transaction);
        return dbContext;
    }

    /// <summary>
    /// How many times a run is tried when PostgreSQL cancels it for a write beside it (<see cref="IsCancelledForAnother"/>):
    /// the first, and two more.
    /// </summary>
    public const int Attempts = 3;

    /// <summary>
    /// Serializable, as IProductVersionRepository numbers versions: a version written in the run has to be numbered as
    /// if nothing else were writing them.
    /// A run PostgreSQL cancels because a transaction beside it got there first (Serializable's "could not serialize access",
    /// or a deadlock) is run again from the start, on a new connection, up to <see cref="Attempts"/> times: nothing it wrote
    /// was kept, and run again it sees what the other committed. So the work must do nothing but its repositories' calls,
    /// which copy what they're given rather than change it, as VersionedSaves' do. A run inside a run isn't tried again
    /// itself: the outer one is.
    /// </summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await InTransactionAsync(work, IsolationLevel.Serializable, readOnly: false, cancellationToken);
            }
            catch (Exception exception) when (attempt < Attempts && Shared() is null && IsCancelledForAnother(exception))
            {
                // A moment's wait, longer each time and never the same, so two runs that clashed don't clash again.
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(10, 50) * attempt), cancellationToken);
            }
        }
    }

    /// <summary>
    /// What PostgreSQL ends a transaction with when another beside it got there first, wrapped however EF Core and Npgsql
    /// wrap it: a serialization failure (40001) or a deadlock (40P01). Trying again can only succeed.
    /// </summary>
    public static bool IsCancelledForAnother(Exception exception)
    {
        for (Exception? inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Repeatable Read and read only: PostgreSQL takes the snapshot at the first query and every later one reads from it,
    /// and such a transaction never fails for a write made beside it, nor makes one fail.
    /// </summary>
    public Task<T> ReadSnapshotAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken) =>
        InTransactionAsync(work, IsolationLevel.RepeatableRead, readOnly: true, cancellationToken);

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, IsolationLevel isolationLevel, bool readOnly, CancellationToken cancellationToken)
    {
        if (Shared() is not null)
        {
            return await work();
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(isolationLevel, cancellationToken);
        if (readOnly)
        {
            await using var command = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

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
