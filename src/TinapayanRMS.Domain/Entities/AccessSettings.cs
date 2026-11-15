namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// The app's one password and how soon an idle window locks itself, set in Settings → Security. The app has one user, so
/// it's a password with no user name. One row at most (Id 1). No row, or a row with no hash, means no password yet: the app
/// asks for one to be set as it opens. The password itself is never kept, only a salted PBKDF2 hash of it
/// (PasswordRules.Hash), with the salt and the iterations it was made with, so a later version can raise the count without
/// losing the passwords made before. scripts/Reset-Password.ps1 takes a forgotten one away.
/// </summary>
public sealed class AccessSettings
{
    /// <summary>The one row's id.</summary>
    public const int SingletonId = 1;

    /// <summary>How many idle minutes lock the window until Settings says otherwise.</summary>
    public const int DefaultAutoLockMinutes = 15;

    public int Id { get; set; } = SingletonId;

    /// <summary>The password's PBKDF2 hash; null with no password set.</summary>
    public byte[]? PasswordHash { get; set; }

    /// <summary>The random salt the hash was made with; null with no password set.</summary>
    public byte[]? PasswordSalt { get; set; }

    /// <summary>The PBKDF2 iterations the hash was made with.</summary>
    public int PasswordIterations { get; set; }

    /// <summary>When the password was last set, in local time.</summary>
    public DateTime? PasswordChangedAt { get; set; }

    /// <summary>Minutes without a key, click or scroll in the window before it locks itself; 0 for never.</summary>
    public int AutoLockMinutes { get; set; } = DefaultAutoLockMinutes;

    /// <summary>Whether a password has been set.</summary>
    public bool HasPassword => PasswordHash is not null && PasswordSalt is not null;
}
