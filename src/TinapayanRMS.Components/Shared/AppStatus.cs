namespace TinapayanRMS.Components.Shared;

/// <summary>
/// What the window needs to know about the app as a whole, one for the process. A database that couldn't be made ready
/// for this version of the app (StartupTasks, over DatabaseReadiness; shown as a banner over every screen, which only a
/// check started signed in gets to see, as the sign-in panel says it first); automatic backups that have
/// stopped working, with the newest one getting old (AutomaticBackups, shown the same way), and a newest backup that
/// wouldn't restore when it was proved; whether the reminder to set a second backup folder was closed; a restore, which
/// replaces what every screen holds, so the window's page loads afresh; and what keeps the window from closing without
/// asking, which Windows wants to know before it ends the app.
/// </summary>
public sealed class AppStatus
{
    private static readonly TimeSpan RestoreNoticeShownFor = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new();
    private (string Message, DateTime At)? _restored;
    private UnsavedChangesGuard? _closingGuard;

    /// <summary>Why some screens won't work, when the database couldn't be made ready; null when it was.</summary>
    public string? DatabaseWarning { get; private set; }

    /// <summary>That automatic backups aren't working and the newest backup is getting old; null while they work.</summary>
    public string? BackupWarning { get; private set; }

    /// <summary>That the newest backup wouldn't restore when it was last proved; null while it did (AutomaticBackups.ProofWarning).</summary>
    public string? BackupProofWarning { get; private set; }

    /// <summary>
    /// Whether the reminder to set Also Copy To has been closed: it stays closed until the app starts again, a page that
    /// loads afresh (after a restore) included.
    /// </summary>
    public bool CopyFolderReminderClosed { get; private set; }

    /// <summary>Raised on whichever thread finished a restore. The window's page reloads, and its notices say what was restored.</summary>
    public event Action? Restored;

    /// <summary>Raised when a warning appears or goes, on whichever thread changed it: the window shows the banner again.</summary>
    public event Action? Changed;

    public void WarnOfDatabase(string warning)
    {
        DatabaseWarning = warning;
        Changed?.Invoke();
    }

    /// <summary>Shows <paramref name="warning"/> over every tab, or takes the backups' warning away with null.</summary>
    public void WarnOfBackups(string? warning)
    {
        if (BackupWarning != warning)
        {
            BackupWarning = warning;
            Changed?.Invoke();
        }
    }

    /// <summary>Shows <paramref name="warning"/> over every tab, or takes the proof's warning away with null.</summary>
    public void WarnOfBackupProof(string? warning)
    {
        if (BackupProofWarning != warning)
        {
            BackupProofWarning = warning;
            Changed?.Invoke();
        }
    }

    /// <summary>Closes the reminder to set Also Copy To until the app starts again.</summary>
    public void CloseCopyFolderReminder()
    {
        CopyFolderReminderClosed = true;
        Changed?.Invoke();
    }

    public void AnnounceRestore(string message)
    {
        lock (_lock)
        {
            _restored = (message, DateTime.UtcNow);
        }

        Restored?.Invoke();
    }

    /// <summary>What the last restore said, while it's recent enough to be the reason a tab just reloaded.</summary>
    public string? RecentRestoreMessage()
    {
        lock (_lock)
        {
            return _restored is { } restored && DateTime.UtcNow - restored.At < RestoreNoticeShownFor ? restored.Message : null;
        }
    }

    /// <summary>
    /// Why the window mustn't close without asking first, as the guard of the page it has loaded says
    /// (<see cref="UnsavedChangesGuard.KeepsWindowOpen"/>); null when it may close, as before any screen is up. Read at
    /// once on the window's thread, the page's too: before a sign-out, a shutdown or an install ends the app, Windows asks
    /// whether it may, and wants the answer there and then (WindowsWindow), with no time to send for the page's.
    /// </summary>
    public string? KeepsWindowOpen => Volatile.Read(ref _closingGuard)?.KeepsWindowOpen;

    /// <summary>Takes <paramref name="guard"/>, the newest page's, as the one <see cref="KeepsWindowOpen"/> asks.</summary>
    internal void Follow(UnsavedChangesGuard guard) => Volatile.Write(ref _closingGuard, guard);

    /// <summary>Lets go of <paramref name="guard"/> as its page goes, unless a newer page's has taken its place.</summary>
    internal void Forget(UnsavedChangesGuard guard) => Interlocked.CompareExchange(ref _closingGuard, null, guard);
}
