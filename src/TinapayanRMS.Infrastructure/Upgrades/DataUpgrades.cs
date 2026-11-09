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

    /// <summary>The same, for EF Core's synchronous Migrate, which `dotnet ef database update` runs, with no context to keep.</summary>
    public static void Run(DbContext context) => RunAsync(context, CancellationToken.None).GetAwaiter().GetResult();

    private static string MigrationId<TMigration>()
        where TMigration : Migration =>
        typeof(TMigration).GetCustomAttribute<MigrationAttribute>()!.Id;
}
