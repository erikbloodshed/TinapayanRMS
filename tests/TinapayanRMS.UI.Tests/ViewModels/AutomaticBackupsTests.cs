using System.IO;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// The day's automatic backup copied to a second folder, which keeps the newest automatic ones as the backups folder does.
/// The backups themselves are files a stand-in writes: taking a real one is DatabaseBackupsTests' part.
/// </summary>
public sealed class AutomaticBackupsTests : IDisposable
{
    private const string Database = "tinapayanrms";

    private readonly string _root = Directory.CreateTempSubdirectory("automatic-backups-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task The_days_backup_is_copied_to_the_second_folder_and_only_the_newest_automatic_ones_are_kept_there()
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

        var backup = await automatic.RunAsync(today, TestContext.Current.CancellationToken);

        File.Exists(Path.Combine(copies, Path.GetFileName(backup!.Path))).Should().BeTrue();
        Directory.GetFiles(copies, "*" + BackupRules.Extension).Should().HaveCount(BackupRules.AutomaticBackupsKept + 1, "the oldest automatic one went, the manual one stays");
        File.Exists(Path.Combine(copies, BackupRules.FileNameFor(Database, BackupKind.Automatic, today.AddDays(-BackupRules.AutomaticBackupsKept)))).Should().BeFalse();
        File.Exists(Path.Combine(copies, manual)).Should().BeTrue();
        File.Exists(Path.Combine(copies, "notes.txt")).Should().BeTrue();
        automatic.LastCopyProblem.Should().BeNull();
    }

    [Fact]
    public async Task A_second_folder_that_isnt_there_is_passed_over_and_said()
    {
        var automatic = Create(Path.Combine(_root, "unplugged"));

        var backup = await automatic.RunAsync(new DateTime(2026, 10, 29, 9, 0, 0), TestContext.Current.CancellationToken);

        backup.Should().NotBeNull("the backup itself is still taken");
        automatic.LastCopyProblem.Should().Contain("wasn't there");
    }

    [Fact]
    public void The_second_folder_is_kept_for_the_user_and_cleared_with_nothing()
    {
        var viewState = new InMemoryViewStateStore();
        var automatic = new AutomaticBackups(new FileBackups(_root), viewState, new LoggerConfiguration().CreateLogger());

        automatic.CopyFolder = @"E:\Backups";
        new AutomaticBackups(new FileBackups(_root), viewState, new LoggerConfiguration().CreateLogger()).CopyFolder.Should().Be(@"E:\Backups");
        automatic.CopyFolder = null;
        automatic.CopyFolder.Should().BeNull();
    }

    private AutomaticBackups Create(string copyFolder) =>
        new(new FileBackups(Directory.CreateDirectory(Path.Combine(_root, "backups")).FullName), new InMemoryViewStateStore(), new LoggerConfiguration().CreateLogger())
        {
            CopyFolder = copyFolder,
        };

    /// <summary>Backups as files in a folder: the day's is due once, and copying one copies its file.</summary>
    private sealed class FileBackups(string folder) : IDatabaseBackups
    {
        public string Folder => folder;

        public IReadOnlyList<DatabaseBackup> List() => [];

        public Task<DatabaseBackup?> BackUpDailyIfDueAsync(DateTime now, CancellationToken cancellationToken)
        {
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
    }
}
