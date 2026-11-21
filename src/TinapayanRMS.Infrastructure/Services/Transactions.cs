using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// The transaction a repository's write runs in: its own, or, inside a unit of work (IUnitOfWork.RunAsync), a part of
/// that one, which the repository commits and disposes as if it were its own while the unit of work decides.
/// <para>
/// Either way it takes the writes' lock first (<see cref="WritesLockKey"/>, a transaction-scoped advisory lock), so writes
/// from two connections take turns: two PCs on one database, or Recalculate Costs beside a save. A write that changes a cost
/// cascades in its own transaction (IngredientCostPropagator), and at Read Committed two that overlapped could each work a
/// made or linked ingredient out from the other's costs as they were before it committed, the one committing last storing
/// a cost from stale inputs that nothing would flag. Taken behind the first, the second reads what the first committed.
/// Every writer takes the one lock before it writes anything, so two can't each hold what the other waits for. A unit of
/// work that writes takes it before its transaction begins (SharedTransactionDbContextFactory.RunAsync), so its snapshot
/// sees what the writer ahead of it committed; a read-only run (ReadSnapshotAsync) takes none and holds up nobody.
/// </para>
/// </summary>
public static class Transactions
{
    /// <summary>The advisory lock every write transaction holds from its first write until it ends: any key nothing else in the database takes ("TRMS").</summary>
    public const long WritesLockKey = 0x54524D53;

    public static async Task<IDbContextTransaction> BeginOrJoinTransactionAsync(this DatabaseFacade database, CancellationToken cancellationToken) =>
        await LockingWritesAsync(
            database,
            database.CurrentTransaction is { } current ? new JoinedTransaction(current) : await database.BeginTransactionAsync(cancellationToken),
            cancellationToken);

    /// <summary>The same, at <paramref name="isolationLevel"/> when it's its own; a unit of work's is Serializable already.</summary>
    public static async Task<IDbContextTransaction> BeginOrJoinTransactionAsync(
        this DatabaseFacade database, IsolationLevel isolationLevel, CancellationToken cancellationToken) =>
        await LockingWritesAsync(
            database,
            database.CurrentTransaction is { } current ? new JoinedTransaction(current) : await database.BeginTransactionAsync(isolationLevel, cancellationToken),
            cancellationToken);

    /// <summary>
    /// <paramref name="transaction"/> once it holds the writes' lock, waiting for whichever transaction holds it to end.
    /// Taken again by a transaction that holds it already, it's held all the same (PostgreSQL counts it), and it's let go
    /// as the transaction commits or rolls back. A transaction that can't take it is let go of, its own or not.
    /// </summary>
    private static async Task<IDbContextTransaction> LockingWritesAsync(DatabaseFacade database, IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({WritesLockKey})", cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

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
