using System.IO;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.App;
using TinapayanRMS.Presentation.Settings;

namespace TinapayanRMS.Presentation.Tests.App;

/// <summary>
/// The day's automatic backup copied to a second folder, which keeps the newest automatic ones as the backups folder does,
/// and what's said when one can't be taken or copied. The backups themselves are files a stand-in writes: taking a real one
/// is DatabaseBackupsTests' part.
/// </summary>
public sealed class AutomaticBackupsTests : IDisposable
{
    private const string Database = "tinapayanrms";

    private static readonly DateTime Today = new(2026, 10, 29, 9, 0, 0);

    private readonly string _root = Directory.CreateTempSubdirectory("automatic-backups-").FullName;

    private static CancellationToken Cancellation => TestContext.Current!.Execution.CancellationToken;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Test]
    public Task The_days_backup_is_copied_to_the_second_folder_and_only_the_newest_automatic_ones_are_kept_there() => UiThread.RunAsync(async () =>
    {
        var copies = Directory.CreateDirectory(Path.Combine(_root, "usb")).FullName;
        var today = new DateTime(2026, 10, 29, 9, 0, 0);
        for (var day = 1; day <= BackupRules.AutomaticBackupsKept; day++)
        {
            File.WriteAllText(Path.Combine(copies, BackupRules.FileNameFor(Database, BackupKind.Automatic, today.AddDays(-day))), "old");
        }

        var manual = BackupRules.FileNameFor(Database, BackupKind.Manual, today.AddDays(-40));
        File.WriteAllText(Path.Combine(copies, manual), "kept");
        File.WriteAllText(Path.Combine(copies, "notes.txt"), "not a backup");
        var automatic = Create(copies);

        var backup = await automatic.RunAsync(today, TestContext.Current!.Execution.CancellationToken);

        File.Exists(Path.Combine(copies, Path.GetFileName(backup!.Path))).Should().BeTrue();
        Directory.GetFiles(copies, "*" + BackupRules.Extension).Should().HaveCount(BackupRules.AutomaticBackupsKept + 1, "the oldest automatic one went, the manual one stays");
        File.Exists(Path.Combine(copies, BackupRules.FileNameFor(Database, BackupKind.Automatic, today.AddDays(-BackupRules.AutomaticBackupsKept)))).Should().BeFalse();
        File.Exists(Path.Combine(copies, manual)).Should().BeTrue();
        File.Exists(Path.Combine(copies, "notes.txt")).Should().BeTrue();
        automatic.LastCopyProblem.Should().BeNull();
    });

    [Test]
    public Task A_second_folder_that_isnt_there_is_passed_over_and_said() => UiThread.RunAsync(async () =>
    {
        var automatic = Create(Path.Combine(_root, "unplugged"));

        var backup = await automatic.RunAsync(new DateTime(2026, 10, 29, 9, 0, 0), TestContext.Current!.Execution.CancellationToken);

        backup.Should().NotBeNull("the backup itself is still taken");
        automatic.LastCopyProblem.Should().Contain("wasn't there");
    });

    [Test]
    public void The_second_folder_is_kept_for_the_user_and_cleared_with_nothing()
    {
        var viewState = new InMemoryViewStateStore();
        var automatic = new AutomaticBackups(new FileBackups(_root), viewState, new LoggerConfiguration().CreateLogger());

        automatic.CopyFolder = @"E:\Backups";
        new AutomaticBackups(new FileBackups(_root), viewState, new LoggerConfiguration().CreateLogger()).CopyFolder.Should().Be(@"E:\Backups");
        automatic.CopyFolder = null;
        automatic.CopyFolder.Should().BeNull();
    }

    [Test]
    public Task A_copys_problem_is_still_said_after_a_restart_until_the_folder_changes() => UiThread.RunAsync(async () =>
    {
        var viewState = new InMemoryViewStateStore();
        var automatic = new AutomaticBackups(new FileBackups(BackupsFolder()), viewState, new LoggerConfiguration().CreateLogger())
        {
            CopyFolder = Path.Combine(_root, "unplugged"),
        };
        await automatic.RunAsync(Today, Cancellation);

        var restarted = new AutomaticBackups(new FileBackups(BackupsFolder()), viewState, new LoggerConfiguration().CreateLogger());
        restarted.LastCopyProblem.Should().Contain("wasn't there");

        restarted.CopyFolder = null;
        restarted.LastCopyProblem.Should().BeNull("Stop Copying leaves no folder to have a problem with");
    });

    [Test]
    public Task A_backup_that_cant_be_taken_says_why_and_stays_said_after_a_restart() => UiThread.RunAsync(async () =>
    {
        var viewState = new InMemoryViewStateStore();
        var backups = new FileBackups(BackupsFolder()) { Refusal = new PostgresFailure("no password supplied") };
        var automatic = new AutomaticBackups(backups, viewState, new LoggerConfiguration().CreateLogger());

        var taken = await automatic.RunAsync(Today, Cancellation);

        taken.Should().BeNull();
        automatic.LastBackupProblem.Should().Contain("wasn't taken").And.Contain("no password supplied");
        new AutomaticBackups(backups, viewState, new LoggerConfiguration().CreateLogger()).LastBackupProblem
            .Should().Be(automatic.LastBackupProblem, "a restart mustn't make it look as if backups are fine");
    });

    [Test]
    public Task The_next_backup_that_goes_through_clears_the_problem() => UiThread.RunAsync(async () =>
    {
        var backups = new FileBackups(BackupsFolder()) { Refusal = new PostgresFailure("no password supplied") };
        var automatic = new AutomaticBackups(backups, new InMemoryViewStateStore(), new LoggerConfiguration().CreateLogger());
        await automatic.RunAsync(Today, Cancellation);
        automatic.LastBackupProblem.Should().NotBeNull();

        backups.Refusal = null;
        await automatic.RunAsync(Today.AddHours(1), Cancellation);

        automatic.LastBackupProblem.Should().BeNull();
    });

    [Test]
    [Arguments(1)]
    [Arguments(2.9)]
    public Task A_failure_with_a_recent_backup_there_interrupts_no_one(double daysOld) => UiThread.RunAsync(async () =>
    {
        var automatic = CreateFailing(newestTakenAt: Today.AddDays(-daysOld));
        await automatic.RunAsync(Today, Cancellation);

        (await automatic.OverdueWarningAsync(Today, Cancellation)).Should().BeNull("the next check may well put it right, and the Database tab says so meanwhile");
    });

    [Test]
    public Task A_failure_with_the_newest_backup_grown_old_says_so_and_why() => UiThread.RunAsync(async () =>
    {
        var automatic = CreateFailing(newestTakenAt: new DateTime(2026, 10, 20, 8, 0, 0));
        await automatic.RunAsync(Today, Cancellation);

        var warning = await automatic.OverdueWarningAsync(Today, Cancellation);

        warning.Should().Contain("October 20, 2026").And.Contain("isn't backed up").And.Contain("no password supplied");
    });

    [Test]
    public Task A_failure_with_no_backup_at_all_says_so() => UiThread.RunAsync(async () =>
    {
        var automatic = CreateFailing(newestTakenAt: null);
        await automatic.RunAsync(Today, Cancellation);

        (await automatic.OverdueWarningAsync(Today, Cancellation)).Should().Contain("no backup").And.Contain("no password supplied");
    });

    [Test]
    public Task The_warning_comes_once_a_run_however_many_hourly_checks_fail() => UiThread.RunAsync(async () =>
    {
        var automatic = CreateFailing(newestTakenAt: null);
        await automatic.RunAsync(Today, Cancellation);
        (await automatic.OverdueWarningAsync(Today, Cancellation)).Should().NotBeNull();

        await automatic.RunAsync(Today.AddHours(1), Cancellation);

        (await automatic.OverdueWarningAsync(Today.AddHours(1), Cancellation)).Should().BeNull();
    });

    [Test]
    public Task An_old_backup_is_no_reason_to_warn_when_todays_went_through() => UiThread.RunAsync(async () =>
    {
        var backups = new FileBackups(BackupsFolder()) { Newest = Backup(Today.AddDays(-30)) };
        var automatic = new AutomaticBackups(backups, new InMemoryViewStateStore(), new LoggerConfiguration().CreateLogger());
        await automatic.RunAsync(Today, Cancellation);

        (await automatic.OverdueWarningAsync(Today, Cancellation)).Should().BeNull();
    });

    [Test]
    public Task The_Database_tab_shows_why_the_last_backup_wasnt_taken() => UiThread.RunAsync(async () =>
    {
        var backups = new FileBackups(BackupsFolder()) { Refusal = new PostgresFailure("no password supplied") };
        var logger = new LoggerConfiguration().CreateLogger();
        var automatic = new AutomaticBackups(backups, new InMemoryViewStateStore(), logger);
        var state = new DatabaseBackupsState(backups, _ => { }, logger, automatic);
        state.BackupProblemText.Should().BeNull();

        await automatic.RunAsync(Today, Cancellation);

        state.BackupProblemText.Should().Contain("no password supplied");
    });

    private string BackupsFolder() => Directory.CreateDirectory(Path.Combine(_root, "backups")).FullName;

    private AutomaticBackups Create(string copyFolder) =>
        new(new FileBackups(BackupsFolder()), new InMemoryViewStateStore(), new LoggerConfiguration().CreateLogger())
        {
            CopyFolder = copyFolder,
        };

    private AutomaticBackups CreateFailing(DateTime? newestTakenAt) =>
        new(
            new FileBackups(BackupsFolder())
            {
                Refusal = new PostgresFailure("no password supplied"),
                Newest = newestTakenAt is { } takenAt ? Backup(takenAt) : null,
            },
            new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());

    private static DatabaseBackup Backup(DateTime takenAt) =>
        new(BackupRules.FileNameFor(Database, BackupKind.Automatic, takenAt), Database, BackupKind.Automatic, takenAt, 5);

    private sealed class PostgresFailure(string message) : Exception(message);

    /// <summary>Backups as files in a folder: the day's is due once, and copying one copies its file.</summary>
    private sealed class FileBackups(string folder) : IDatabaseBackups
    {
        /// <summary>What taking the day's backup fails with; null when it goes through.</summary>
        public Exception? Refusal { get; set; }

        public DatabaseBackup? Newest { get; set; }

        public string Folder => folder;

        public void EnsureFolder() => throw new NotSupportedException();

        public IReadOnlyList<DatabaseBackup> List() => [];

        public Task<DatabaseBackup?> NewestAsync(CancellationToken cancellationToken) => Task.FromResult(Newest);

        public Task<DatabaseBackup?> BackUpDailyIfDueAsync(DateTime now, CancellationToken cancellationToken)
        {
            if (Refusal is { } refusal)
            {
                return Task.FromException<DatabaseBackup?>(refusal);
            }

            var path = Path.Combine(folder, BackupRules.FileNameFor(Database, BackupKind.Automatic, now));
            File.WriteAllText(path, "today");
            return Task.FromResult<DatabaseBackup?>(new DatabaseBackup(path, Database, BackupKind.Automatic, now, 5));
        }

        public Task CopyToAsync(DatabaseBackup backup, string destinationPath, CancellationToken cancellationToken)
        {
            File.Copy(backup.Path, destinationPath, overwrite: true);
            return Task.CompletedTask;
        }

        public Task<DatabaseBackup> BackUpAsync(BackupKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DatabaseBackup> CopyInAsync(string sourcePath, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BackupHeader> InspectAsync(DatabaseBackup backup, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RestoredBackup> RestoreAsync(DatabaseBackup backup, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Delete(DatabaseBackup backup) => throw new NotSupportedException();

        public Task<bool> RecoverInterruptedRestoreAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
