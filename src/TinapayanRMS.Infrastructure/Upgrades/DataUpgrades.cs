using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using TinapayanRMS.Infrastructure.Migrations;

namespace TinapayanRMS.Infrastructure.Upgrades;

/// <summary>
/// The one-time changes to the data that a schema change needs, each made once on each database, as it's migrated. EF
/// Core runs them through its seeding hook (TinapayanRMSDbContext.Configure) after the migrations and inside their
/// transaction, whether the database is updated by `dotnet ef database update` or a restored backup is brought up to date,
/// so a new column and the data it needs are written together. Each is recorded in data_upgrades and never made again.
/// Migrations only change the schema: they come only from `dotnet ef migrations add`.
/// </summary>
internal static class DataUpgrades
{
    /// <summary>
    /// Every upgrade, oldest first: its name, the migration whose schema it needs, and what it does. It runs against the
    /// schema the migrations leave, so it can't read a column a later migration drops.
    /// </summary>
    private static readonly IReadOnlyList<(string Id, string Migration, Func<TinapayanRMSDbContext, CancellationToken, Task> Run)> All =
    [
        ("2026-11-05 section names", MigrationId<_20261105040552_StoreSectionNames>(), SectionNamesUpgrade.RunAsync),
        ("2026-11-05 target buffers", MigrationId<RawMaterialTargetCostBuffer>(), TargetBuffersUpgrade.RunAsync),
        ("2026-11-09 innovation stage", MigrationId<InitialCreate>(), InnovationStageUpgrade.RunAsync),
        ("2026-11-12 product folders", MigrationId<_20261112122740_ProductFolders>(), ProductFoldersUpgrade.RunAsync),
        ("2026-11-26 roles into other sections", MigrationId<_20261126144828_RoleCanGoIntoOtherSections>(), RolesIntoOtherSectionsUpgrade.RunAsync),
    ];

    /// <summary>The migration that made data_upgrades, which no upgrade can be recorded without.</summary>
    private static readonly string TableMigration = MigrationId<_20261105040552_StoreSectionNames>();

    /// <summary>
    /// Makes every upgrade <paramref name="context"/>'s database hasn't had yet, in order, recording each. One whose migration
    /// isn't applied waits, as when a database is migrated back to an earlier migration.
    /// </summary>
    public static async Task RunAsync(DbContext context, CancellationToken cancellationToken)
    {
        var dbContext = (TinapayanRMSDbContext)context;
        HashSet<string> migrations = [.. await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)];
        if (!migrations.Contains(TableMigration))
        {
            return;
        }

        HashSet<string> applied = [.. await dbContext.DataUpgrades.Select(u => u.Id).ToListAsync(cancellationToken)];
        foreach (var (id, _, run) in All.Where(u => migrations.Contains(u.Migration) && !applied.Contains(u.Id)))
        {
            await run(dbContext, cancellationToken);
            dbContext.DataUpgrades.Add(new DataUpgrade { Id = id, AppliedAt = dbContext.LocalNow() });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The upgrades <paramref name="dbContext"/>'s database is due and hasn't had: their migration is applied, but they
    /// aren't recorded. One added to the app after its migration ran on this database (tied to an earlier migration, as
    /// "2026-11-09 innovation stage" is to InitialCreate) is one, and only a migrate makes it (<see cref="RunAsync"/>, EF
    /// Core's seeding), so the updater counts it as an update even with no migration to apply (DatabaseUpdater, and a
    /// restore's DatabaseBackups.BringUpToDateAsync). Those of a migration not applied yet come with it.
    /// </summary>
    public static async Task<IReadOnlyList<string>> OverdueAsync(
        TinapayanRMSDbContext dbContext, IReadOnlyCollection<string> appliedMigrations, CancellationToken cancellationToken)
    {
        if (!appliedMigrations.Contains(TableMigration))
        {
            return [];
        }

        HashSet<string> made = [.. await dbContext.DataUpgrades.Select(u => u.Id).ToListAsync(cancellationToken)];
        return [.. All.Where(u => appliedMigrations.Contains(u.Migration) && !made.Contains(u.Id)).Select(u => u.Id)];
    }

    /// <summary>The same, for EF Core's synchronous Migrate, which `dotnet ef database update` runs, with no context to keep.</summary>
    // EF Core's seeding hook for Migrate is synchronous, and only dotnet-ef's own process, with no UI thread, calls it: the
    // app migrates with MigrateAsync, which seeds by RunAsync.
#pragma warning disable RS0030 // The one wait on async work allowed in Infrastructure (approved 2026-11-22, ADR 0010).
    public static void Run(DbContext context) => RunAsync(context, CancellationToken.None).GetAwaiter().GetResult();
#pragma warning restore RS0030

    private static string MigrationId<TMigration>()
        where TMigration : Migration =>
        typeof(TMigration).GetCustomAttribute<MigrationAttribute>()!.Id;
}
