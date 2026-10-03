using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Services;

/// <summary>
/// Real backups and restores by SQL Server, of a database of each test's own (ThrowawayDatabase) into a folder of its own
/// under ProgramData, set up as the app's is. In the database collection, so they run one at a time with the other database
/// tests: a restore clears every pooled connection.
/// </summary>
[Collection(DatabaseCollection.Name)]
[SupportedOSPlatform("windows")]
public sealed class DatabaseBackupsTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TinapayanRMS", "Test Backups", Guid.NewGuid().ToString("N"));

    private ThrowawayDatabase _database = null!;
    private DatabaseBackups _backups = null!;

    public async ValueTask InitializeAsync()
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

    [Fact]
    public async Task A_backup_is_written_to_a_folder_SQL_Server_and_every_user_can_use()
    {
        var backup = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);

        File.Exists(backup.Path).Should().BeTrue();
        backup.Kind.Should().Be(BackupKind.Manual);
        backup.SizeBytes.Should().BeGreaterThan(0);
        Path.GetFileName(backup.Path).Should().StartWith(_database.Name + "_").And.EndWith("_manual.bak");
        _backups.List().Should().ContainSingle().Which.Path.Should().Be(backup.Path);

        var rules = new DirectoryInfo(_folder).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>().ToList();
        var service = new NTAccount(BackupRules.ServiceAccountFor("SQLEXPRESS")).Translate(typeof(SecurityIdentifier));
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        foreach (var account in new[] { service, users })
        {
            rules.Should().Contain(rule => rule.IdentityReference == account
                && rule.AccessControlType == AccessControlType.Allow
                && (rule.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify);
        }
    }

    [Fact]
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

    [Fact]
    public async Task A_backup_from_an_earlier_version_is_brought_up_to_date()
    {
        string[] migrations;
        await using (var dbContext = _database.CreateDbContext())
        {
            migrations = [.. dbContext.Database.GetMigrations()];
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

    [Fact]
    public async Task A_backup_from_a_later_version_is_refused_and_nothing_changes()
    {
        await AddCategoryAsync("Breads");
        await ExecuteAsync("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('99991231000000_FromTheFuture', '99.0.0')");
        var later = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);
        await ExecuteAsync("DELETE FROM __EFMigrationsHistory WHERE MigrationId = '99991231000000_FromTheFuture'");
        await AddCategoryAsync("Cakes");

        var restore = () => _backups.RestoreAsync(later, Cancellation);

        await restore.Should().ThrowAsync<BusinessRuleException>().WithMessage(BackupRules.LaterVersionText);
        (await CategoryNamesAsync()).Should().Equal("Breads", "Cakes");
        await using var dbContext = _database.CreateDbContext();
        (await dbContext.Database.GetAppliedMigrationsAsync(Cancellation)).Should().NotContain("99991231000000_FromTheFuture");
    }

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task A_file_that_isnt_a_backup_is_refused_by_SQL_Server()
    {
        var notABackup = Path.Combine(Path.GetTempPath(), $"not a backup {Guid.NewGuid():N}.bak");
        await File.WriteAllTextAsync(notABackup, "Pandesal, 4.50", Cancellation);
        try
        {
            var copy = await _backups.CopyInAsync(notABackup, Cancellation);

            var inspect = () => _backups.InspectAsync(copy, Cancellation);

            await inspect.Should().ThrowAsync<SqlException>();
        }
        finally
        {
            File.Delete(notABackup);
        }
    }

    [Fact]
    public async Task A_backup_copied_out_and_back_in_restores()
    {
        await AddCategoryAsync("Breads");
        var backup = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);
        var onTheStick = Path.Combine(Path.GetTempPath(), $"stick {Guid.NewGuid():N}.bak");
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

    [Fact]
    public async Task Copying_a_backup_onto_itself_leaves_it_whole()
    {
        var backup = await _backups.BackUpAsync(BackupKind.Manual, Cancellation);

        await _backups.CopyToAsync(backup, backup.Path, Cancellation);

        new FileInfo(backup.Path).Length.Should().Be(backup.SizeBytes);
    }

    private async Task AddCategoryAsync(string name)
    {
        await using var dbContext = _database.CreateDbContext();
        dbContext.RawMaterialCategories.Add(new RawMaterialCategory { Name = name, Type = RawMaterialType.Ingredient });
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
