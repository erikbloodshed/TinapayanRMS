namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// A screen running something that closing the window would cut short: Settings → Database's backups, copies and restores.
/// The window ends the process as it closes, so a restore cut short between its renames would leave the app's database
/// under another name, and the app unable to open it (DatabaseBackups.RecoverInterruptedRestoreAsync puts it back at the
/// next start). The window asks every one before it closes, and stays open while any says why not.
/// </summary>
public interface IBusyWork
{
    /// <summary>Why the window mustn't close now, in words for the user; null while nothing closing would cut short is running.</summary>
    string? KeepsWindowOpen { get; }
}
