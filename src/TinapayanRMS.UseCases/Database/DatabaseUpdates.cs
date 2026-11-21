using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.UseCases.Database;

/// <summary>
/// The database made ready for this version of the app as it opens (DatabaseReadiness runs it once per start): a restore
/// cut short put back, the database made if it isn't there, backed up and updated if it's behind, refused if a newer copy
/// of the app updated it.
/// </summary>
public interface IDatabaseUpdates
{
    /// <summary>
    /// Makes the database ready; <paramref name="applying"/> is told how many updates are about to be applied, when any are.
    /// What was done, for the log.
    /// </summary>
    Task<DatabaseUpdate> UpdateAsync(Action<int>? applying, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IDatabaseUpdates"/>
public sealed class DatabaseUpdates(IDatabaseUpdater updater) : IDatabaseUpdates
{
    public Task<DatabaseUpdate> UpdateAsync(Action<int>? applying, CancellationToken cancellationToken) => updater.UpdateAsync(applying, cancellationToken);
}
