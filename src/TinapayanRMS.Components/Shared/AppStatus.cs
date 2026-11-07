namespace TinapayanRMS.Components.Shared;

/// <summary>
/// What the window needs to know about the app as a whole, one for the process. The database missing updates this
/// version of the app needs (checked at startup, shown as a banner over every screen, as the WPF app said so in a message
/// box); automatic backups that have stopped working, with the newest one getting old (AutomaticBackups, shown the same
/// way); and a restore, which replaces what every screen holds, so the window's page loads afresh, as the WPF app restarted.
/// </summary>
public sealed class AppStatus
{
    private static readonly TimeSpan RestoreNoticeShownFor = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new();
    private (string Message, DateTime At)? _restored;

    /// <summary>Why some screens won't work, when the database lacks updates; null when it's up to date.</summary>
    public string? DatabaseWarning { get; private set; }

    /// <summary>That automatic backups aren't working and the newest backup is getting old; null while they work.</summary>
    public string? BackupWarning { get; private set; }

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
}
