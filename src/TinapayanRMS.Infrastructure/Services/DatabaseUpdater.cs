using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Infrastructure.Upgrades;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// <see cref="IDatabaseUpdater"/> over EF Core's migrations, as a restore brings an older backup up to date
/// (DatabaseBackups). EF Core takes its migration lock while it applies them, so two copies opening the same database at
/// once don't both apply them: the second finds nothing left to do. The data upgrades run with them, as the migration's
/// seeding (TinapayanRMSDbContext.UseAsyncSeeding), and an upgrade added after its migration ran here counts as an update
/// too (DataUpgrades.OverdueAsync): with no migration to apply, only a migrate would make it.
/// </summary>
/// <param name="backups">Takes the backup before an update. A backup that can't be taken stops the update: none goes ahead without a way back.</param>
public sealed class DatabaseUpdater(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory, IDatabaseBackups backups) : IDatabaseUpdater
{
    public async Task<DatabaseUpdate> UpdateAsync(Action<int>? applying, CancellationToken cancellationToken) =>
        await UpdateAsync(await backups.RecoverInterruptedRestoreAsync(cancellationToken), applying, cancellationToken);

    private async Task<DatabaseUpdate> UpdateAsync(bool recovered, Action<int>? applying, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var database = dbContext.Database;
        List<string> known = [.. database.GetMigrations()];

        // A new PC, or a database name not used before: made, with every update, and nothing to back up.
        if (!await database.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken))
        {
            applying?.Invoke(known.Count);
            await database.MigrateAsync(cancellationToken);
            return new DatabaseUpdate(known.Count, null, Created: true, recovered);
        }

        List<string> applied = [.. await database.GetAppliedMigrationsAsync(cancellationToken)];

        // An empty database, made by hand to put the app in: given every update, as a new one is.
        if (applied.Count == 0 && !await HasTablesAsync(dbContext, cancellationToken))
        {
            applying?.Invoke(known.Count);
            await database.MigrateAsync(cancellationToken);
            return new DatabaseUpdate(known.Count, null, Created: false, recovered);
        }

        var needed = BackupRules.DatabaseUpdatesNeeded(applied, known);
        var updates = needed.Count + (await DataUpgrades.OverdueAsync(dbContext, applied, cancellationToken)).Count;
        if (updates == 0)
        {
            return new DatabaseUpdate(0, null, Created: false, recovered);
        }

        applying?.Invoke(updates);
        var backup = await backups.BackUpAsync(BackupKind.BeforeUpdate, cancellationToken);
        await database.MigrateAsync(cancellationToken);
        return new DatabaseUpdate(updates, backup, Created: false, recovered);
    }

    private static async Task<bool> HasTablesAsync(TinapayanRMSDbContext dbContext, CancellationToken cancellationToken) =>
        await dbContext.Database
            .SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema')) AS \"Value\"")
            .SingleAsync(cancellationToken);
}
