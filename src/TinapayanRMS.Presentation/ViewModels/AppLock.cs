namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// Whether the window is locked, one for the process, so the page loading again after a restore keeps it. It starts
/// locked: nothing of the screens is drawn until the password is given (SignInViewModel). Once signed in, the title bar's
/// Lock, or no key, click or scroll in the window for the auto-lock's minutes (<see cref="CheckIdle"/>), locks it again,
/// and the screens stay as they were under the sign-in panel, unsaved edits and all. Five wrong passwords in a row make
/// the next try wait half a minute. Its changes come on whichever thread made them (<see cref="Changed"/>).
/// </summary>
public sealed class AppLock(TimeProvider clock)
{
    /// <summary>Wrong passwords in a row before the next try has to wait.</summary>
    public const int WrongTriesBeforeWait = 5;

    /// <summary>How long a try waits after <see cref="WrongTriesBeforeWait"/> wrong ones.</summary>
    public static readonly TimeSpan WaitAfterWrongTries = TimeSpan.FromSeconds(30);

    /// <summary>How often the window asks whether it has been idle long enough (<see cref="CheckIdle"/>).</summary>
    public static readonly TimeSpan CheckIdleEvery = TimeSpan.FromSeconds(30);

    private readonly Lock _lock = new();
    private bool _isLocked = true;
    private bool _hasSignedIn;
    private int _autoLockMinutes = Domain.Entities.AccessSettings.DefaultAutoLockMinutes;
    private DateTimeOffset _lastActivity = clock.GetUtcNow();
    private int _wrongTries;
    private DateTimeOffset _waitUntil = DateTimeOffset.MinValue;

    /// <summary>Raised when the window locks or unlocks.</summary>
    public event Action? Changed;

    public bool IsLocked
    {
        get
        {
            lock (_lock)
            {
                return _isLocked;
            }
        }
    }

    /// <summary>Whether the password has been given since the app started: until then no screen is drawn at all.</summary>
    public bool HasSignedIn
    {
        get
        {
            lock (_lock)
            {
                return _hasSignedIn;
            }
        }
    }

    /// <summary>Idle minutes before the window locks itself; 0 for never.</summary>
    public int AutoLockMinutes
    {
        get
        {
            lock (_lock)
            {
                return _autoLockMinutes;
            }
        }
    }

    /// <summary>How long the next try still has to wait after too many wrong ones; zero when it needn't.</summary>
    public TimeSpan WaitLeft
    {
        get
        {
            lock (_lock)
            {
                var left = _waitUntil - clock.GetUtcNow();
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }
    }

    /// <summary>The password was given: the screens show, and the idle time starts again.</summary>
    public void Unlock()
    {
        lock (_lock)
        {
            _wrongTries = 0;
            _waitUntil = DateTimeOffset.MinValue;
            _lastActivity = clock.GetUtcNow();
            if (!_isLocked)
            {
                return;
            }

            _isLocked = false;
            _hasSignedIn = true;
        }

        Changed?.Invoke();
    }

    /// <summary>Locks the window (the title bar's Lock, or the auto-lock).</summary>
    public void Lock()
    {
        lock (_lock)
        {
            if (_isLocked)
            {
                return;
            }

            _isLocked = true;
        }

        Changed?.Invoke();
    }

    /// <summary>A key, click or scroll in the window: the idle time starts again.</summary>
    public void NoteActivity()
    {
        lock (_lock)
        {
            _lastActivity = clock.GetUtcNow();
        }
    }

    /// <summary>Locks the window when it's been idle for the auto-lock's minutes, and says whether it did.</summary>
    public bool CheckIdle()
    {
        lock (_lock)
        {
            if (_isLocked || _autoLockMinutes <= 0 || clock.GetUtcNow() - _lastActivity < TimeSpan.FromMinutes(_autoLockMinutes))
            {
                return false;
            }
        }

        Lock();
        return true;
    }

    public void SetAutoLockMinutes(int minutes)
    {
        lock (_lock)
        {
            _autoLockMinutes = Math.Max(0, minutes);
        }
    }

    /// <summary>A wrong password: the fifth in a row makes the next try wait <see cref="WaitAfterWrongTries"/>.</summary>
    public void NoteWrongTry()
    {
        lock (_lock)
        {
            if (++_wrongTries >= WrongTriesBeforeWait)
            {
                _wrongTries = 0;
                _waitUntil = clock.GetUtcNow() + WaitAfterWrongTries;
            }
        }
    }
}
