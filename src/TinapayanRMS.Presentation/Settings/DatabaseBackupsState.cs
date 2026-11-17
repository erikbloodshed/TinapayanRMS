using System.Collections.ObjectModel;
using System.Reactive;
using TinapayanRMS.ApplicationCore.Interfaces;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.App;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Settings;

/// <summary>A row of the Database tab's backups: when it was taken, what took it, and how big it is.</summary>
public sealed class DatabaseBackupRow(DatabaseBackup backup)
{
    public DatabaseBackup Backup => backup;

    public DateTime TakenAt => backup.TakenAt;

    public string Kind => backup.Kind switch
    {
        BackupKind.Manual => "Manual",
        BackupKind.Automatic => "Automatic",
        BackupKind.BeforeRestore => "Before restore",
        BackupKind.BeforeUpdate => "Before update",
        _ => "Copied in",
    };

    /// <summary>In megabytes, as a file's size is usually read.</summary>
    public decimal SizeMegabytes => backup.SizeBytes / (1024m * 1024m);
}

/// <summary>
/// Settings' Database tab: the backups in the backups folder, newest first, and what can be done with them. Open Folder
/// shows the folder in File Explorer; Back Up Now takes one; Copy to… puts one elsewhere, such as a USB stick; Restore and Restore from File…
/// put one back over the database, after a confirmation, and restart the app, whose screens all hold what was there before.
/// </summary>
public sealed class DatabaseBackupsState : ScreenState, IBusyWork
{
    private readonly IDatabaseBackups _backups;
    private readonly Action<string> _restartApp;

    /// <summary>Set while a restore runs: closing then would cut it short, and the app restarts by itself once it's done.</summary>
    private bool _restoring;

    public ObservableCollection<DatabaseBackupRow> Backups { get; } = [];

    private DatabaseBackupRow? _selectedBackup;
    public DatabaseBackupRow? SelectedBackup
    {
        get => _selectedBackup;
        set => RaiseAndSetIfChanged(ref _selectedBackup, value);
    }

    /// <summary>Where the backups are kept, shown so they can be found in File Explorer.</summary>
    public string Folder => _backups.Folder;

    /// <summary>Input: the backups folder, to show. The page opens it in File Explorer (IFileDialogs.ShowFolder).</summary>
    public Interaction<string, Unit> ShowFolder { get; } = new();

    /// <summary>Shows the backups folder in File Explorer, making it first if no backup has yet.</summary>
    public ScreenCommand OpenFolderCommand { get; }

    private string? _busyText;
    /// <summary>What's being done while a backup, copy or restore runs, which can take a while; null otherwise.</summary>
    public string? BusyText
    {
        get => _busyText;
        private set => RaiseAndSetIfChanged(ref _busyText, value);
    }

    /// <inheritdoc/>
    public string? KeepsWindowOpen => BusyText is null ? null
        : _restoring ? "A restore is running, and closing now would cut it short. TinapayanRMS restarts by itself once it's done."
        : $"Settings → Database is still at work ({BusyText.TrimEnd('…')}). Close TinapayanRMS once it's done.";

    /// <summary>Input: the backup's file name, to suggest. Output: where to copy it, or null when cancelled. The page shows the Save dialog (IFileDialogs).</summary>
    public Interaction<string, string?> ChooseCopyDestination { get; } = new();

    /// <summary>Output: a backup file to restore, or null when cancelled. The page shows the Open dialog (IFileDialogs).</summary>
    public Interaction<Unit, string?> ChooseBackupFile { get; } = new();

    public ScreenCommand LoadCommand { get; }

    public ScreenCommand BackUpNowCommand { get; }

    public ScreenCommand CopyToCommand { get; }

    public ScreenCommand RestoreCommand { get; }

    public ScreenCommand RestoreFromFileCommand { get; }

    /// <summary>What takes the day's backup and copies it to the second folder; null in a screen built without it.</summary>
    private readonly AutomaticBackups? _automatic;

    /// <summary>Where each automatic backup is copied as well; "None" when nowhere.</summary>
    public string CopyFolderText => _automatic?.CopyFolder ?? "None";

    /// <summary>Why the last automatic copy didn't go through, in amber under the folder; null when it did.</summary>
    public string? CopyProblemText => _automatic?.LastCopyProblem;

    /// <summary>Why the last automatic backup wasn't taken, in amber under the backups folder; null when it was.</summary>
    public string? BackupProblemText => _automatic?.LastBackupProblem;

    /// <summary>Output: the folder to copy automatic backups to, or null when cancelled. The page shows the folder dialog (IFileDialogs).</summary>
    public Interaction<Unit, string?> ChooseCopyFolder { get; } = new();

    /// <summary>Picks the second folder each automatic backup is copied to, then copies the newest automatic one there now.</summary>
    public ScreenCommand ChooseCopyFolderCommand { get; }

    /// <summary>Stops copying automatic backups anywhere else. What was copied stays.</summary>
    public ScreenCommand ClearCopyFolderCommand { get; }

    /// <param name="restartApp">Closes the app and starts it again, after showing the message it's given.</param>
    public DatabaseBackupsState(IDatabaseBackups backups, Action<string> restartApp, ILogger logger, AutomaticBackups? automatic = null)
        : base(logger)
    {
        _backups = backups;
        _restartApp = restartApp;
        _automatic = automatic;

        OpenFolderCommand = new ScreenCommand(() => RunSafelyAsync(async () =>
        {
            _backups.EnsureFolder();
            await ShowFolder.Handle(Folder);
        }));

        ChooseCopyFolderCommand = new ScreenCommand(
            () => RunSafelyAsync(async () =>
            {
                var folder = await ChooseCopyFolder.Handle(Unit.Default);
                if (folder is null || _automatic is null)
                {
                    return;
                }

                _automatic.CopyFolder = folder;
                var newest = Backups.Select(r => r.Backup).Where(b => b.Kind == BackupKind.Automatic).MaxBy(b => b.TakenAt);
                if (newest is not null)
                {
                    await WhileBusyAsync("Copying…", () => Task.Run(() => _automatic.CopyAsync(newest, CancellationToken.None)));
                }

                RaiseAutomaticBackupsChanged();
                SuccessMessage = CopyProblemText is null
                    ? $"Each automatic backup is copied to {folder} too{(newest is null ? "." : ", starting with the newest.")}"
                    : null;
            }),
            () => automatic is not null);
        ClearCopyFolderCommand = ScreenCommand.Of(
            () =>
            {
                _automatic!.CopyFolder = null;
                RaiseAutomaticBackupsChanged();
            },
            () => automatic is not null);

        bool Idle() => BusyText is null;
        bool IdleWithBackup() => BusyText is null && SelectedBackup is not null;

        LoadCommand = new ScreenCommand(() => LoadSafelyAsync(async () =>
        {
            await RefreshAsync(SelectedBackup?.Backup.Path);
            RaiseAutomaticBackupsChanged();
        }));

        BackUpNowCommand = new ScreenCommand(
            () => RunSafelyAsync(() => WhileBusyAsync("Backing up…", async () =>
            {
                var backup = await Task.Run(() => _backups.BackUpAsync(BackupKind.Manual, CancellationToken.None));
                await RefreshAsync(backup.Path);
                SuccessMessage = $"Backed up to {Path.GetFileName(backup.Path)}.";
            })),
            Idle);

        CopyToCommand = new ScreenCommand(
            () => RunSafelyAsync(async () =>
            {
                var backup = SelectedBackup!.Backup;
                var destination = await ChooseCopyDestination.Handle(Path.GetFileName(backup.Path));
                if (destination is null)
                {
                    return;
                }

                await WhileBusyAsync("Copying…", () => Task.Run(() => _backups.CopyToAsync(backup, destination, CancellationToken.None)));
                SuccessMessage = $"Copied the backup to {destination}.";
            }),
            IdleWithBackup);

        RestoreCommand = new ScreenCommand(
            () => RunSafelyAsync(() => RestoreAsync(SelectedBackup!.Backup)),
            IdleWithBackup);

        // A file from elsewhere goes into the backups folder first, to be restored from there. If it's then refused or
        // not restored after all, the copy goes again.
        RestoreFromFileCommand = new ScreenCommand(
            () => RunSafelyAsync(async () =>
            {
                var file = await ChooseBackupFile.Handle(Unit.Default);
                if (file is null)
                {
                    return;
                }

                var copy = await WhileBusyAsync("Copying the file in…", () => Task.Run(() => _backups.CopyInAsync(file, CancellationToken.None)));
                var restored = false;
                try
                {
                    restored = await RestoreAsync(copy);
                }
                finally
                {
                    if (!restored)
                    {
                        _backups.Delete(copy);
                    }
                }
            }),
            Idle);
    }

    /// <summary>
    /// Asks before restoring <paramref name="backup"/>, giving the day it was taken from the file itself, then restores it
    /// and restarts the app. False when the user said no.
    /// </summary>
    private async Task<bool> RestoreAsync(DatabaseBackup backup)
    {
        var header = await Task.Run(() => _backups.InspectAsync(backup, CancellationToken.None));
        if (!await ConfirmDelete.Handle(ConfirmRestoreText(header)))
        {
            return false;
        }

        _restoring = true;
        try
        {
            var restored = await WhileBusyAsync(
                "Restoring… TinapayanRMS restarts when it's done.",
                () => Task.Run(() => _backups.RestoreAsync(backup, CancellationToken.None)));
            _restartApp(RestoredText(header, restored));
            return true;
        }
        finally
        {
            _restoring = false;
        }
    }

    public static string ConfirmRestoreText(BackupHeader header) =>
        $"Restore the backup taken {header.TakenAt:MMMM d, yyyy 'at' h:mm tt}?"
        + Environment.NewLine + Environment.NewLine
        + "All of the app's data is replaced with the backup's. What was saved since it was taken is lost, and so is anything "
        + "not saved yet on the other screens. A safety backup of the data as it is now is taken first."
        + Environment.NewLine + Environment.NewLine
        + "A backup from an earlier version of TinapayanRMS is brought up to date. TinapayanRMS restarts when it's done.";

    public static string RestoredText(BackupHeader header, RestoredBackup restored) =>
        $"Restored the backup taken {header.TakenAt:MMMM d, yyyy 'at' h:mm tt}."
        + (restored.UpdatesApplied switch
        {
            0 => string.Empty,
            1 => " It was brought up to date with 1 app update.",
            var count => $" It was brought up to date with {count} app updates.",
        })
        + Environment.NewLine + Environment.NewLine
        + $"The data from before is in the safety backup {Path.GetFileName(restored.SafetyBackup.Path)}. TinapayanRMS restarts now.";

    /// <summary>Lists the backups again, picking the one at <paramref name="selectPath"/> if it's still there.</summary>
    private async Task RefreshAsync(string? selectPath)
    {
        var backups = await Task.Run(_backups.List);
        Backups.Clear();
        foreach (var backup in backups)
        {
            Backups.Add(new DatabaseBackupRow(backup));
        }

        SelectedBackup = Backups.FirstOrDefault(row => row.Backup.Path == selectPath);
    }

    private async Task<T> WhileBusyAsync<T>(string text, Func<Task<T>> work)
    {
        BusyText = text;
        try
        {
            return await work();
        }
        finally
        {
            BusyText = null;
        }
    }

    private async Task WhileBusyAsync(string text, Func<Task> work)
    {
        BusyText = text;
        try
        {
            await work();
        }
        finally
        {
            BusyText = null;
        }
    }

    private void RaiseAutomaticBackupsChanged()
    {
        RaisePropertyChanged(nameof(CopyFolderText));
        RaisePropertyChanged(nameof(CopyProblemText));
        RaisePropertyChanged(nameof(BackupProblemText));
    }
}
