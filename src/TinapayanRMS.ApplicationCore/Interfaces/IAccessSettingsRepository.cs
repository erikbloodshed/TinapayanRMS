using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// The app's password and auto-lock (AccessSettings). A password is hashed and checked off the calling thread
/// (PasswordRules, about a third of a second each).
/// </summary>
public interface IAccessSettingsRepository
{
    /// <summary>The one row, or one with no password and the default auto-lock while there's none.</summary>
    Task<AccessSettings> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets the first password, refusing one that breaks PasswordRules, and refusing outright once a password is set
    /// (changing one takes the current one: <see cref="ChangePasswordAsync"/>).
    /// </summary>
    Task CreatePasswordAsync(string password, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="password"/> is the one set; false while none is.</summary>
    Task<bool> VerifyAsync(string password, CancellationToken cancellationToken);

    /// <summary>Replaces the password with <paramref name="newPassword"/>, refusing a wrong <paramref name="currentPassword"/>.</summary>
    Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken);

    /// <summary>Sets the idle minutes before the window locks itself, 0 to PasswordRules.MaxAutoLockMinutes (0 for never).</summary>
    Task SetAutoLockMinutesAsync(int minutes, CancellationToken cancellationToken);
}
