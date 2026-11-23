using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Services;

/// <summary>
/// The app bringing its own database up to its version as it opens: nothing done to one that's current, a backup then the
/// updates for one that's behind, a new one made, and one a newer copy updated, or someone else's, left alone. Each test
/// has a database of its own (ThrowawayDatabase) and a backups folder of its own, as DatabaseBackupsTests have.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public sealed class DatabaseUpdaterTests(DatabaseFixture fixture) : IAsyncDisposable
{
    private static CancellationToken Cancellation => TestContext.Current!.Execution.CancellationToken;

    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TinapayanRMS", "Test Backups", Guid.NewGuid().ToString("N"));

    private ThrowawayDatabase _database = null!;
    private DatabaseBackups _backups = null!;

    [Before(Test)]
    public async Task CreateDatabaseAsync()
    {
        _database = await ThrowawayDatabase.CreateAsync(fixture.ConnectionString, Cancellation);
        _backups = new DatabaseBackups(_database.CreateDbContextFactory(), _folder);
    }

    public async ValueTask DisposeAsync()
    {
        _backups.Dispose();
        await _database.DisposeAsync();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private DatabaseUpdater CreateUpdater(IDatabaseBackups? backups = null) => new(_database.CreateDbContextFactory(), backups ?? _backups);

    private async Task<string[]> MigrationsAsync()
    {
        await using var dbContext = _database.CreateDbContext();
        return [.. dbContext.Database.GetMigrations()];
    }

    private async Task<List<string>> AppliedAsync()
    {
        await using var dbContext = _database.CreateDbContext();
        return [.. await dbContext.Database.GetAppliedMigrationsAsync(Cancellation)];
    }

    private async Task TakeBackOneMigrationAsync()
    {
        var migrations = await MigrationsAsync();
        await using var dbContext = _database.CreateDbContext();
        await dbContext.GetService<IMigrator>().MigrateAsync(migrations[^2], cancellationToken: Cancellation);
    }

    // Not EnsureDeleted: Npgsql drops WITH (FORCE), which also tries to end an autovacuum worker, and an account that isn't
    // a superuser may not (ThrowawayDatabase drops the same way).
    private async Task DropDatabaseAsync()
    {
        NpgsqlConnection.ClearAllPools();
        var maintenance = new NpgsqlConnectionStringBuilder(_database.ConnectionString) { Database = "postgres", Pooling = false };
        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{_database.Name}\"", connection);
        await command.ExecuteNonQueryAsync(Cancellation);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var dbContext = _database.CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync(sql, Cancellation);
    }

    private async Task AddCategoryAsync(string name)
    {
        await using var dbContext = _database.CreateDbContext();
        dbContext.RawMaterialCategories.Add(new RawMaterialCategory { Name = name, Type = RawMaterialCategoryType.Ingredient });
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private async Task<List<string>> CategoryNamesAsync()
    {
        await using var dbContext = _database.CreateDbContext();
        return await dbContext.RawMaterialCategories.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync(Cancellation);
    }

    /// <summary>
    /// The database as a copy of the app before the "2026-11-09 innovation stage" upgrade left it, with a product still
    /// stored in Innovation's former stage, and the upgrade not recorded: as if the app had just gained it, its migration
    /// (InitialCreate) long applied.
    /// </summary>
    private async Task ForgetInnovationUpgradeAsync()
    {
        await using (var dbContext = _database.CreateDbContext())
        {
            dbContext.Products.Add(new Product { Name = "Ube Pandesal", Stage = ProductStage.Innovation });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await ExecuteAsync("UPDATE products SET stage = 'ResearchAndDevelopment'");
        await ExecuteAsync("DELETE FROM data_upgrades WHERE id = '2026-11-09 innovation stage'");
    }

    /// <summary>The stages as stored, and whether the innovation upgrade is recorded.</summary>
    private async Task<(List<string> Stages, bool Recorded)> InnovationUpgradeAsync()
    {
        await using var dbContext = _database.CreateDbContext();
        var stages = await dbContext.Database.SqlQuery<string>($"SELECT stage AS \"Value\" FROM products").ToListAsync(Cancellation);
        var recorded = await dbContext.DataUpgrades.AnyAsync(u => u.Id == "2026-11-09 innovation stage", Cancellation);
        return (stages, recorded);
    }

    // An upgrade the app gained after its migration ran here: with no migration to apply, only a migrate makes it, so it
    // counts as an update, a backup first (DataUpgrades.OverdueAsync).
    [Test]
    public async Task A_data_upgrade_added_after_its_migration_ran_is_made_after_a_backup()
    {
        await ForgetInnovationUpgradeAsync();
        var told = 0;

        var update = await CreateUpdater().UpdateAsync(count => told = count, Cancellation);

        (update.Applied, update.Created).Should().Be((1, false));
        told.Should().Be(1, "the panel says it's updating");
        update.Backup.Should().NotBeNull().And.Match<DatabaseBackup>(b => b.Kind == BackupKind.BeforeUpdate);
        (await InnovationUpgradeAsync()).Should().BeEquivalentTo((Stages: new List<string> { "Innovation" }, Recorded: true));

        (await CreateUpdater().UpdateAsync(null, Cancellation)).Applied.Should().Be(0, "made once, it's recorded");
    }

    [Test]
    public async Task A_database_already_up_to_date_is_left_alone_with_no_backup()
    {
        var told = 0;

        var update = await CreateUpdater().UpdateAsync(_ => told++, Cancellation);

        update.Should().Be(new DatabaseUpdate(0, null, Created: false));
        told.Should().Be(0, "nothing was applied, so the panel never says it's updating");
        _backups.List().Should().BeEmpty();
    }

    [Test]
    public async Task A_database_behind_is_backed_up_then_brought_up_to_date_with_its_data_kept()
    {
        var migrations = await MigrationsAsync();
        Skip.When(migrations.Length < 2, "There's only one migration so far, so no earlier version of the database.");
        await TakeBackOneMigrationAsync();
        await AddCategoryAsync("Breads");
        int? told = null;

        var update = await CreateUpdater().UpdateAsync(count => told = count, Cancellation);

        update.Applied.Should().Be(1);
        told.Should().Be(1);
        update.Backup.Should().NotBeNull();
        update.Backup!.Kind.Should().Be(BackupKind.BeforeUpdate);
        _backups.List().Should().ContainSingle(b => b.Kind == BackupKind.BeforeUpdate);
        (await AppliedAsync()).Should().Equal(migrations);
        (await CategoryNamesAsync()).Should().Equal("Breads");
    }

    [Test]
    public async Task The_backup_before_an_update_is_of_the_database_as_it_was_and_restores_it()
    {
        var migrations = await MigrationsAsync();
        Skip.When(migrations.Length < 2, "There's only one migration so far, so no earlier version of the database.");
        await TakeBackOneMigrationAsync();

        var update = await CreateUpdater().UpdateAsync(null, Cancellation);

        var header = await _backups.InspectAsync(update.Backup!, Cancellation);
        header.DatabaseName.Should().Be(_database.Name);
        var restored = await _backups.RestoreAsync(update.Backup!, Cancellation);
        restored.UpdatesApplied.Should().Be(1, "it held the database from before the update");
    }

    [Test]
    public async Task A_backup_that_cant_be_taken_stops_the_update_with_nothing_applied()
    {
        var migrations = await MigrationsAsync();
        Skip.When(migrations.Length < 2, "There's only one migration so far, so no earlier version of the database.");
        await TakeBackOneMigrationAsync();
        // pg_dump with no password to be had, as another Windows user or a lost pgpass.conf comes to.
        var noPassword = new NpgsqlConnectionStringBuilder(_database.ConnectionString) { Passfile = Path.Combine(_folder, "no such file.conf") };
        using var cantBackUp = new DatabaseBackups(
            new SharedTransactionDbContextFactory(noPassword.ConnectionString), _folder, toolTimeout: TimeSpan.FromSeconds(30));

        var update = () => CreateUpdater(cantBackUp).UpdateAsync(null, Cancellation);

        await update.Should().ThrowAsync<Exception>();
        (await AppliedAsync()).Should().Equal(migrations[..^1], "no update goes ahead without a way back");
    }

    [Test]
    public async Task A_database_a_newer_copy_updated_is_refused_and_left_as_it_was()
    {
        await AddCategoryAsync("Breads");
        await ExecuteAsync("""INSERT INTO "__EFMigrationsHistory" (migration_id, product_version) VALUES ('99991231000000_FromTheFuture', '99.0.0')""");

        var update = () => CreateUpdater().UpdateAsync(null, Cancellation);

        await update.Should().ThrowAsync<BusinessRuleException>().WithMessage(BackupRules.DatabaseFromLaterVersionText);
        (await AppliedAsync()).Should().Contain("99991231000000_FromTheFuture");
        (await CategoryNamesAsync()).Should().Equal("Breads");
        _backups.List().Should().BeEmpty();
    }

    [Test]
    public async Task A_database_not_there_yet_is_made_with_every_update()
    {
        await DropDatabaseAsync();

        var update = await CreateUpdater().UpdateAsync(null, Cancellation);

        update.Created.Should().BeTrue();
        update.Backup.Should().BeNull();
        (await AppliedAsync()).Should().Equal(await MigrationsAsync());
    }

    [Test]
    public async Task An_empty_database_is_given_every_update()
    {
        await DropDatabaseAsync();
        await using (var dbContext = _database.CreateDbContext())
        {
            await dbContext.GetService<IRelationalDatabaseCreator>().CreateAsync(Cancellation);
        }

        var update = await CreateUpdater().UpdateAsync(null, Cancellation);

        update.Created.Should().BeFalse();
        update.Applied.Should().Be((await MigrationsAsync()).Length);
        (await AppliedAsync()).Should().Equal(await MigrationsAsync());
    }

    [Test]
    public async Task A_database_with_tables_but_none_of_the_apps_updates_is_someone_elses_and_left_alone()
    {
        await DropDatabaseAsync();
        await using (var dbContext = _database.CreateDbContext())
        {
            await dbContext.GetService<IRelationalDatabaseCreator>().CreateAsync(Cancellation);
        }

        await ExecuteAsync("CREATE TABLE orders (id integer PRIMARY KEY)");

        var update = () => CreateUpdater().UpdateAsync(null, Cancellation);

        await update.Should().ThrowAsync<BusinessRuleException>().WithMessage(BackupRules.DatabaseNotThisAppsText);
        (await AppliedAsync()).Should().BeEmpty();
    }
}
