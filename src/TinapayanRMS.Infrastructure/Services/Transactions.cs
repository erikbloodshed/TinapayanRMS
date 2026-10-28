using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// The transaction a repository's write runs in: its own, or, inside a unit of work (IUnitOfWork.RunAsync), a part of
/// that one, which the repository commits and disposes as if it were its own while the unit of work decides.
/// </summary>
public static class Transactions
{
    public static Task<IDbContextTransaction> BeginOrJoinTransactionAsync(this DatabaseFacade database, CancellationToken cancellationToken) =>
        database.CurrentTransaction is { } current
            ? Task.FromResult<IDbContextTransaction>(new JoinedTransaction(current))
            : database.BeginTransactionAsync(cancellationToken);

    /// <summary>The same, at <paramref name="isolationLevel"/> when it's its own; a unit of work's is Serializable already.</summary>
    public static Task<IDbContextTransaction> BeginOrJoinTransactionAsync(
        this DatabaseFacade database, IsolationLevel isolationLevel, CancellationToken cancellationToken) =>
        database.CurrentTransaction is { } current
            ? Task.FromResult<IDbContextTransaction>(new JoinedTransaction(current))
            : database.BeginTransactionAsync(isolationLevel, cancellationToken);

    /// <summary>
    /// A part in a unit of work's transaction: committing it commits nothing, and disposing it leaves the transaction open,
    /// so the unit of work commits it, or rolls it all back when anything in it throws.
    /// </summary>
    private sealed class JoinedTransaction(IDbContextTransaction joined) : IDbContextTransaction
    {
        public Guid TransactionId => joined.TransactionId;

        public void Commit()
        {
        }

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        // Nothing rolls back by hand; a unit of work rolls back when its work throws, which is how a part should end it.
        public void Rollback() => throw new InvalidOperationException("A part of a unit of work rolls back by throwing.");

        public Task RollbackAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("A part of a unit of work rolls back by throwing.");

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
