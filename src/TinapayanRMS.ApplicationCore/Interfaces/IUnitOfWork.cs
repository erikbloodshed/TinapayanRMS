namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// Several repositories' calls as one transaction, saved together or not at all: a Save on the Products screen with the
/// versions it records, for one. Every repository call the work makes, however deep, joins it, and a repository's own
/// transaction inside it becomes part of this one.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="work"/> in one transaction, committed when it returns and rolled back when it throws; inside
    /// one already, it joins that one. Its reads share one connection, so they must run one at a time, never side by side.
    /// </summary>
    Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken);
}
