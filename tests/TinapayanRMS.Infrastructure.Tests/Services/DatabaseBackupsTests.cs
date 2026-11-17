using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
/// Real backups and restores by pg_dump and pg_restore, of a database of each test's own (ThrowawayDatabase) into a
/// folder of its own under ProgramData, set up as the app's is. In the database collection, so they run one at a time with the other database tests: a restore
/// clears every pooled connection.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public sealed class DatabaseBackupsTests(DatabaseFixture fixture) : IAsyncDisposable
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

    [Test]
    [SupportedOSPlatform("windows")]
    public async Task A_backup_is_written_to_a_folder_every_windows_user_can_use()
    {
        await _backups.BackUpAsync(BackupKind.Manual, Cancellation);

        FolderRules().Should().Contain(rule => AllowsUsersToModify(rule));
    }

    [Test]
    [SupportedOSPlatform("windows")]
    public void The_folder_made_before_any_backup_is_one_every_windows_user_can_use()
    {
        _backups.EnsureFolder();

        FolderRules().Should().Contain(rule => AllowsUsersToModify(rule));
        _backups.List().Should().BeEmpty("making the folder takes no backup");
    }

    [SupportedOSPlatform("windows")]
    private IEnumerable<FileSystemAccessRule> FolderRules() =>
        new DirectoryInfo(_folder).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>();

    [SupportedOSPlatform("windows")]
    private static bool AllowsUsersToModify(FileSystemAccessRule rule) =>
        rule.IdentityReference == new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)
        && rule.AccessControlType == AccessControlType.Allow
        && (rule.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify;

    [Test]
    public async Task A_backup_is_written_to_the_folder_and_listed()
    {
        var backup = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);

        File.Exists(backup.Path).Should().BeTrue();
        backup.Kind.Should().Be(BackupKind.Manual);
        backup.SizeBytes.Should().BeGreaterThan(0);
        Path.GetFileName(backup.Path).Should().StartWith(_database.Name + "_").And.EndWith("_manual.backup");
        _backups.List().Should().ContainSingle().Which.Path.Should().Be(backup.Path);
    }

    [Test]
    public async Task A_backup_leaves_only_itself_in_the_folder()
    {
        var backup = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);

        Directory.GetFiles(_folder).Should().ContainSingle().Which.Should().Be(backup.Path, "the partial file it was written to has taken its name");
    }

    /// <summary>
    /// What another Windows user, or a lost pgpass.conf, comes to. Asked to prompt, pg_dump waits on a console nobody can see
    /// for ever; this one's timeout only keeps a regression from hanging the test run, as it would hang the app's backups.
    /// </summary>
    [Test]
    public async Task With_no_password_to_be_had_a_backup_fails_at_once_and_says_why()
    {
        var noPassword = new NpgsqlConnectionStringBuilder(_database.ConnectionString) { Passfile = Path.Combine(_folder, "no such file.conf") };
        using var backups = new DatabaseBackups(new SharedTransactionDbContextFactory(noPassword.ConnectionString), _folder, toolTimeout: TimeSpan.FromSeconds(30));

        var backUp = () => backups.BackUpAsync(BackupKind.Manual, Cancellation);

        (await backUp.Should().ThrowAsync<PostgresToolException>()).Which.Error.Should().Contain("no password supplied");
        Directory.GetFiles(_folder).Should().BeEmpty("nothing of it is left behind");
    }

    /// <summary>pg_dump waits for as long as a table it needs is held, and nothing else ends that wait. What it left isn't a backup, and it holds no one up.</summary>
    [Test]
    public async Task A_tool_that_hangs_is_stopped_and_lets_the_next_backup_through()
    {
        var today = new DateTime(2026, 10, 3, 8, 0, 0);
        using var backups = new DatabaseBackups(_database.CreateDbContextFactory(), _folder, toolTimeout: TimeSpan.FromSeconds(3));
        await using (var holder = _database.CreateDbContext())
        {
            await using var holding = await holder.Database.BeginTransactionAsync(Cancellation);
            await holder.Database.ExecuteSqlRawAsync("LOCK TABLE raw_material_categories IN ACCESS EXCLUSIVE MODE", Cancellation);

            var backUp = () => backups.BackUpDailyIfDueAsync(today, Cancellation);

            await backUp.Should().ThrowAsync<TimeoutException>();
            backups.List().Should().BeEmpty("what it wrote before it was stopped isn't a backup");
        }

        var next = await backups.BackUpDailyIfDueAsync(today, Cancellation);

        next.Should().NotBeNull("the one that was stopped wasn't the day's backup, and let go of its turn");
        backups.List().Should().ContainSingle().Which.Path.Should().Be(next!.Path);
    }

    [Test]
    public async Task A_partial_file_isnt_a_backup_its_name_isnt_reused_and_it_goes_once_its_old()
    {
        Directory.CreateDirectory(_folder);
        var today = new DateTime(2026, 10, 3, 8, 0, 0);
        var name = BackupRules.FileNameFor(_database.Name, BackupKind.Automatic, today);
        var abandoned = Path.Combine(_folder, name + BackupRules.PartialSuffix);
        var stillBeingWritten = Path.Combine(_folder, BackupRules.FileNameFor(_database.Name, BackupKind.Manual, today) + BackupRules.PartialSuffix);
        await File.WriteAllTextAsync(abandoned, "cut short", Cancellation);
        File.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddDays(-2));
        await File.WriteAllTextAsync(stillBeingWritten, "in another copy of the app", Cancellation);

        _backups.List().Should().BeEmpty("neither reads as a backup");
        (await _backups.NewestAsync(Cancellation)).Should().BeNull();

        var taken = await _backups.BackUpDailyIfDueAsync(today, Cancellation);

        taken.Should().NotBeNull("a file cut short isn't the day's backup");
        Path.GetFileName(taken!.Path).Should().NotBe(name, "that name was in use");
        File.Exists(abandoned).Should().BeFalse("nothing will ever finish it");
        File.Exists(stillBeingWritten).Should().BeTrue("another copy of the app may still be writing it");
    }

    [Test]
    [SupportedOSPlatform("windows")]
    public async Task A_copy_that_doesnt_finish_leaves_the_file_it_would_replace_as_it_was()
    {
        var backup = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);
        var stick = Directory.CreateTempSubdirectory("backup-stick-").FullName;
        try
        {
            var destination = Path.Combine(stick, Path.GetFileName(backup.Path));
            await File.WriteAllTextAsync(destination, "yesterday's", Cancellation);

            // Cut short once it has started writing: another program has locked the backup, so the copy opens it, and its
            // partial file beside the destination, but can't read it.
            await using (var locked = new FileStream(backup.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                locked.Lock(0, locked.Length);
                var cutShort = () => _backups.CopyToAsync(backup, destination, Cancellation);

                await cutShort.Should().ThrowAsync<IOException>();
                locked.Unlock(0, locked.Length);
            }

            (await File.ReadAllTextAsync(destination, Cancellation)).Should().Be("yesterday's");
            Directory.GetFiles(stick).Should().ContainSingle("nothing of the copy is left behind");

            // Cancelled as it waits for its turn, it writes nothing at all.
            var cancelled = () => _backups.CopyToAsync(backup, destination, new CancellationToken(canceled: true));

            await cancelled.Should().ThrowAsync<OperationCanceledException>();
            (await File.ReadAllTextAsync(destination, Cancellation)).Should().Be("yesterday's");

            await _backups.CopyToAsync(backup, destination, Cancellation);

            new FileInfo(destination).Length.Should().Be(backup.SizeBytes, "a copy that finishes replaces it");
            Directory.GetFiles(stick).Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(stick, recursive: true);
        }
    }

    /// <summary>
    /// The day's backup is copied to the second folder from the background while Settings may be copying one there, and two
    /// copies to one file at once fought over its partial file: so a copy takes its turn, as backups and restores do. The
    /// backup under way here is held up by a lock until the tools' timeout stops it, so a copy that didn't wait would be
    /// done long before it.
    /// </summary>
    [Test]
    public async Task A_copy_waits_its_turn_behind_a_backup_under_way()
    {
        var backup = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);
        using var backups = new DatabaseBackups(_database.CreateDbContextFactory(), _folder, toolTimeout: TimeSpan.FromSeconds(3));
        var stick = Directory.CreateTempSubdirectory("backup-stick-").FullName;
        try
        {
            var destination = Path.Combine(stick, Path.GetFileName(backup.Path));
            await using (var holder = _database.CreateDbContext())
            {
                await using var holding = await holder.Database.BeginTransactionAsync(Cancellation);
                await holder.Database.ExecuteSqlRawAsync("LOCK TABLE raw_material_categories IN ACCESS EXCLUSIVE MODE", Cancellation);
                var heldUp = backups.BackUpAsync(BackupKind.Manual, Cancellation);
                await UntilABackupWaitsForALockAsync(heldUp);

                await backups.CopyToAsync(backup, destination, Cancellation);

                heldUp.IsCompleted.Should().BeTrue("the copy waited for the backup under way to let go of its turn");
                var stopped = () => heldUp;
                await stopped.Should().ThrowAsync<TimeoutException>();
            }

            new FileInfo(destination).Length.Should().Be(backup.SizeBytes, "the copy went through once it had its turn");
        }
        finally
        {
            Directory.Delete(stick, recursive: true);
        }
    }

    [Test]
    public async Task The_newest_backup_is_the_one_just_taken()
    {
        (await _backups.NewestAsync(Cancellation)).Should().BeNull("there are none yet");

        await _backups.BackUpDailyIfDueAsync(new DateTime(2026, 10, 2, 8, 0, 0), Cancellation);
        var newest = await _backups.BackUpDailyIfDueAsync(new DateTime(2026, 10, 3, 8, 0, 0), Cancellation);

        (await _backups.NewestAsync(Cancellation))!.Path.Should().Be(newest!.Path);
    }

    [Test]
    public async Task A_backup_says_which_database_it_is_of_and_when_it_was_taken()
    {
        // The listing gives the time to the second.
        var before = DateTime.Now.AddSeconds(-1);
        var backup = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);

        var header = await _backups.InspectAsync(backup, Cancellation);

        header.DatabaseName.Should().Be(_database.Name);
        header.TakenAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.Now);
    }

    [Test]
    public async Task A_restore_puts_the_data_back_as_it_was_after_a_safety_backup()
    {
        await AddCategoryAsync("Breads");
        var backup = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);
        await AddCategoryAsync("Cakes");

        var restored = await _backups.RestoreAsync(backup, Cancellation);

        (await CategoryNamesAsync()).Should().Equal("Breads");
        restored.UpdatesApplied.Should().Be(0);
        restored.SafetyBackup.Kind.Should().Be(BackupKind.BeforeRestore);
        File.Exists(restored.SafetyBackup.Path).Should().BeTrue();

        // The safety backup is the data from just before: Cakes comes back with it.
        await _backups.RestoreAsync(restored.SafetyBackup, Cancellation);
        (await CategoryNamesAsync()).Should().Equal("Breads", "Cakes");
    }

    [Test]
    public async Task A_restored_database_carries_on_numbering_where_the_backup_left_off()
    {
        await AddCategoryAsync("Breads");
        var backup = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);
        await _backups.RestoreAsync(backup, Cancellation);

        var add = () => AddCategoryAsync("Cakes");

        await add.Should().NotThrowAsync("the identity sequences come back with the data, so a new row doesn't take a restored row's id");
        (await CategoryNamesAsync()).Should().Equal("Breads", "Cakes");
    }

    [Test]
    public async Task Two_backups_taken_straight_after_each_other_are_two_files_and_neither_overwrites_the_other()
    {
        await AddCategoryAsync("Breads");
        var first = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);
        await AddCategoryAsync("Cakes");

        var second = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);

        second.Path.Should().NotBe(first.Path, "names go to the second, and a name taken moves on a second");
        _backups.List().Select(b => b.Path).Should().Contain([first.Path, second.Path]);
        await _backups.RestoreAsync(first, Cancellation);
        (await CategoryNamesAsync()).Should().Equal(["Breads"], "the first backup still holds what it was taken with");
    }

    [Test]
    public async Task A_backup_from_an_earlier_version_is_brought_up_to_date()
    {
        string[] migrations;
        await using (var dbContext = _database.CreateDbContext())
        {
            migrations = [.. dbContext.Database.GetMigrations()];
        }

        Skip.When(migrations.Length < 2, "There's only one migration so far, so no earlier version of the database to back up.");
        await using (var dbContext = _database.CreateDbContext())
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(migrations[^2], cancellationToken: Cancellation);
        }

        var older = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);
        await using (var dbContext = _database.CreateDbContext())
        {
            await dbContext.Database.MigrateAsync(Cancellation);
        }

        var restored = await _backups.RestoreAsync(older, Cancellation);

        restored.UpdatesApplied.Should().Be(1);
        await using (var dbContext = _database.CreateDbContext())
        {
            (await dbContext.Database.GetPendingMigrationsAsync(Cancellation)).Should().BeEmpty();
        }
    }

    [Test]
    public async Task A_backup_from_a_later_version_is_refused_and_nothing_changes()
    {
        await AddCategoryAsync("Breads");
        await ExecuteAsync("""INSERT INTO "__EFMigrationsHistory" (migration_id, product_version) VALUES ('99991231000000_FromTheFuture', '99.0.0')""");
        var later = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);
        await ExecuteAsync("""DELETE FROM "__EFMigrationsHistory" WHERE migration_id = '99991231000000_FromTheFuture'""");
        await AddCategoryAsync("Cakes");

        var restore = () => _backups.RestoreAsync(later, Cancellation);

        await restore.Should().ThrowAsync<BusinessRuleException>().WithMessage(BackupRules.LaterVersionText);
        (await CategoryNamesAsync()).Should().Equal("Breads", "Cakes");
        await using var dbContext = _database.CreateDbContext();
        (await dbContext.Database.GetAppliedMigrationsAsync(Cancellation)).Should().NotContain("99991231000000_FromTheFuture");
    }

    [Test]
    public async Task The_daily_backup_is_taken_once_a_day()
    {
        var today = new DateTime(2026, 10, 3, 8, 0, 0);

        var first = await _backups.BackUpDailyIfDueAsync(today, Cancellation);
        var again = await _backups.BackUpDailyIfDueAsync(today.AddHours(5), Cancellation);
        var tomorrow = await _backups.BackUpDailyIfDueAsync(today.AddDays(1), Cancellation);

        first.Should().NotBeNull();
        first!.Kind.Should().Be(BackupKind.Automatic);
        again.Should().BeNull();
        tomorrow.Should().NotBeNull();
    }

    [Test]
    public async Task Only_the_newest_fourteen_automatic_backups_are_kept()
    {
        Directory.CreateDirectory(_folder);
        for (var day = 1; day <= 15; day++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_folder, BackupRules.FileNameFor(_database.Name, BackupKind.Automatic, new DateTime(2026, 9, day, 8, 0, 0))), "old", Cancellation);
        }

        var manual = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);
        await _backups.BackUpDailyIfDueAsync(new DateTime(2026, 10, 3, 8, 0, 0), Cancellation);

        var automatic = _backups.List().Where(b => b.Kind == BackupKind.Automatic).ToList();
        automatic.Should().HaveCount(BackupRules.AutomaticBackupsKept);
        automatic.Min(b => b.TakenAt).Should().Be(new DateTime(2026, 9, 3, 8, 0, 0));
        File.Exists(manual.Path).Should().BeTrue("only automatic backups are ever deleted");
    }

    [Test]
    public async Task A_file_that_isnt_a_backup_is_refused_before_anything_changes()
    {
        await AddCategoryAsync("Breads");
        var notABackup = Path.Combine(Path.GetTempPath(), $"not a backup {Guid.NewGuid():N}.backup");
        await File.WriteAllTextAsync(notABackup, "Pandesal, 4.50", Cancellation);
        try
        {
            var copy = await _backups.CopyInAsync(notABackup, Cancellation);

            var inspect = () => _backups.InspectAsync(copy, Cancellation);
            var restore = () => _backups.RestoreAsync(copy, Cancellation);

            await inspect.Should().ThrowAsync<BusinessRuleException>().WithMessage(BackupRules.NotADatabaseBackupText);
            await restore.Should().ThrowAsync<BusinessRuleException>().WithMessage(BackupRules.NotADatabaseBackupText);
            (await CategoryNamesAsync()).Should().Equal("Breads");
            _backups.List().Should().NotContain(b => b.Kind == BackupKind.BeforeRestore, "it's refused before the safety backup is taken");
        }
        finally
        {
            File.Delete(notABackup);
        }
    }

    [Test]
    public async Task A_backup_copied_out_and_back_in_restores()
    {
        await AddCategoryAsync("Breads");
        var backup = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);
        var onTheStick = Path.Combine(Path.GetTempPath(), $"stick {Guid.NewGuid():N}.backup");
        try
        {
            await _backups.CopyToAsync(backup, onTheStick, Cancellation);
            await AddCategoryAsync("Cakes");

            var copy = await _backups.CopyInAsync(onTheStick, Cancellation);
            var header = await _backups.InspectAsync(copy, Cancellation);
            await _backups.RestoreAsync(copy, Cancellation);

            copy.Kind.Should().Be(BackupKind.CopiedIn);
            header.DatabaseName.Should().Be(_database.Name);
            (await CategoryNamesAsync()).Should().Equal("Breads");
        }
        finally
        {
            File.Delete(onTheStick);
        }
    }

    [Test]
    public async Task A_backup_of_another_database_restores_over_this_one()
    {
        await AddCategoryAsync("Breads");
        await using var other = await ThrowawayDatabase.CreateAsync(fixture.ConnectionString, Cancellation);
        await using (var dbContext = other.CreateDbContext())
        {
            dbContext.RawMaterialCategories.Add(new RawMaterialCategory { Name = "From the other computer", Type = RawMaterialCategoryType.Ingredient });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        using var otherBackups = new DatabaseBackups(other.CreateDbContextFactory(), _folder);
        var fromElsewhere = await otherBackups.BackUpAsync(BackupKind.Manual, Cancellation);

        await _backups.RestoreAsync(fromElsewhere, Cancellation);

        (await CategoryNamesAsync()).Should().Equal("From the other computer");
    }

    [Test]
    public async Task Copying_a_backup_onto_itself_leaves_it_whole()
    {
        var backup = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);

        await _backups.CopyToAsync(backup, backup.Path, Cancellation);

        new FileInfo(backup.Path).Length.Should().Be(backup.SizeBytes);
    }

    // The app ended as a restore put its restored copy in place, between renaming the database aside and renaming the
    // copy into its place: nothing has the database's name, and every screen would fail to load.
    [Test]
    public async Task A_restore_cut_short_between_its_renames_is_put_back_at_the_next_start()
    {
        await AddCategoryAsync("Breads");
        var replaced = _database.Name + DatabaseBackups.ReplacedSuffix;
        try
        {
            NpgsqlConnection.ClearAllPools();
            await MaintenanceAsync($"ALTER DATABASE \"{_database.Name}\" WITH ALLOW_CONNECTIONS false");
            await MaintenanceAsync($"ALTER DATABASE \"{_database.Name}\" RENAME TO \"{replaced}\"");

            (await _backups.RecoverInterruptedRestoreAsync(Cancellation)).Should().BeTrue();

            (await CategoryNamesAsync()).Should().Equal("Breads");
        }
        finally
        {
            await MaintenanceAsync($"DROP DATABASE IF EXISTS \"{replaced}\"");
        }
    }

    // A restore that swapped its copy in but was cut short before it dropped the one it replaced: the database is there,
    // and nothing of the one set aside is the app's to touch (the next restore drops it first).
    [Test]
    public async Task Nothing_is_put_back_while_the_database_is_there()
    {
        await AddCategoryAsync("Breads");
        var replaced = _database.Name + DatabaseBackups.ReplacedSuffix;
        try
        {
            await MaintenanceAsync($"CREATE DATABASE \"{replaced}\" TEMPLATE template0");

            (await _backups.RecoverInterruptedRestoreAsync(Cancellation)).Should().BeFalse();

            (await CategoryNamesAsync()).Should().Equal("Breads");
        }
        finally
        {
            await MaintenanceAsync($"DROP DATABASE IF EXISTS \"{replaced}\"");
        }

        (await _backups.RecoverInterruptedRestoreAsync(Cancellation)).Should().BeFalse("with none set aside there's nothing to put back");
    }

    /// <summary>
    /// Waits until a backup's pg_dump is held up by a lock on this test's database, its turn taken, or until
    /// <paramref name="backUp"/> has ended without that.
    /// </summary>
    private async Task UntilABackupWaitsForALockAsync(Task backUp)
    {
        var settings = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = "postgres", Pooling = false };
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT FROM pg_stat_activity WHERE datname = @database AND wait_event_type = 'Lock')", connection);
        command.Parameters.AddWithValue("database", _database.Name);
        while (!backUp.IsCompleted && !(bool)(await command.ExecuteScalarAsync(Cancellation))!)
        {
            await Task.Delay(25, Cancellation);
        }
    }

    /// <summary>A statement on the server's maintenance database, unpooled, as the app's own run there.</summary>
    private async Task MaintenanceAsync(string sql)
    {
        var settings = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = "postgres", Pooling = false, CommandTimeout = 600 };
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Cancellation);
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

    private async Task ExecuteAsync(string sql)
    {
        await using var dbContext = _database.CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync(sql, Cancellation);
    }
}
