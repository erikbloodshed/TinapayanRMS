using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UseCases.Access;

/// <summary>
/// The app's password and the idle minutes before the window locks itself (<see cref="AccessSettings"/>), kept in the
/// database: what the sign-in panel and Settings' Security tab read and write. What's typed is checked by the screens
/// (PasswordRules) before it's asked for here. A refusal is a <see cref="BusinessRuleException"/>, in words for the
/// shop's staff.
/// </summary>
public interface IAccessSettingsEditor
{
    /// <summary>Whether a password is set, and the auto-lock's minutes.</summary>
    Task<AccessSettings> GetAsync(CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="password"/> is the one set; false with none set.</summary>
    Task<bool> VerifyAsync(string password, CancellationToken cancellationToken);

    /// <summary>Sets the first password, with none set yet; refused once one is.</summary>
    Task CreatePasswordAsync(string password, CancellationToken cancellationToken);

    /// <summary>Changes the password, given the current one; refused when it isn't.</summary>
    Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken);

    /// <summary>Saves the idle minutes before the window locks itself, 0 for never.</summary>
    Task SetAutoLockMinutesAsync(int minutes, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IAccessSettingsEditor"/>
public sealed class AccessSettingsEditor(IAccessSettingsRepository repository) : IAccessSettingsEditor
{
    public Task<AccessSettings> GetAsync(CancellationToken cancellationToken) => repository.GetAsync(cancellationToken);

    public Task<bool> VerifyAsync(string password, CancellationToken cancellationToken) => repository.VerifyAsync(password, cancellationToken);

    public Task CreatePasswordAsync(string password, CancellationToken cancellationToken) => repository.CreatePasswordAsync(password, cancellationToken);

    public Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken) =>
        repository.ChangePasswordAsync(currentPassword, newPassword, cancellationToken);

    public Task SetAutoLockMinutesAsync(int minutes, CancellationToken cancellationToken) => repository.SetAutoLockMinutesAsync(minutes, cancellationToken);
}
