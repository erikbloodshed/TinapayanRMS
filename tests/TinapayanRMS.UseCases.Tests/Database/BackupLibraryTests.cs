using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.UseCases.Database;

namespace TinapayanRMS.UseCases.Tests.Database;

/// <summary>The backups folder as the screens see it: Back Up Now takes a manual backup, which the Database tab shows as Manual.</summary>
public class BackupLibraryTests
{
    [Test]
    public async Task Back_Up_Now_takes_a_manual_backup()
    {
        var backups = new RecordingBackups();

        var taken = await new BackupLibrary(backups).BackUpNowAsync(CancellationToken.None);

        backups.Kinds.Should().Equal(BackupKind.Manual);
        taken.Kind.Should().Be(BackupKind.Manual);
    }

    /// <summary>Takes nothing, but says what it was asked to take.</summary>
    private sealed class RecordingBackups : IDatabaseBackups
    {
        public List<BackupKind> Kinds { get; } = [];

        public string Folder => @"C:\Backups";

        public Task<DatabaseBackup> BackUpAsync(BackupKind kind, CancellationToken cancellationToken)
        {
            Kinds.Add(kind);
            return Task.FromResult(new DatabaseBackup(@"C:\Backups\taken.backup", "tinapayanrms", kind, new DateTime(2026, 11, 21, 9, 0, 0), 1));
        }

        public void EnsureFolder() => throw new NotSupportedException();

        public IReadOnlyList<DatabaseBackup> List() => throw new NotSupportedException();

        public Task<DatabaseBackup?> NewestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DatabaseBackup?> BackUpDailyIfDueAsync(DateTime now, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DatabaseBackup> CopyInAsync(string sourcePath, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CopyToAsync(DatabaseBackup backup, string destinationPath, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BackupHeader> InspectAsync(DatabaseBackup backup, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RestoredBackup> RestoreAsync(DatabaseBackup backup, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Delete(DatabaseBackup backup) => throw new NotSupportedException();

        public Task<bool> RecoverInterruptedRestoreAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BackupProof> ProveAsync(DatabaseBackup backup, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
