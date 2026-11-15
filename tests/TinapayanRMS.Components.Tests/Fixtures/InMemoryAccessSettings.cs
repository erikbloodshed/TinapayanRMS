using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Components.Tests.Fixtures;

/// <summary>
/// The app's password kept in memory, for the sign-in panel's component tests, which have no database: hashed and checked
/// by the same PasswordRules as AccessSettingsRepository, whose own tests (Infrastructure.Tests) cover the database.
/// </summary>
public sealed class InMemoryAccessSettings : IAccessSettingsRepository
{
    private AccessSettings _settings = new();

    /// <summary>While set, a check of a password waits for it, as the real one waits for its hashing and the database.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public Task<AccessSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Copy());

    public Task CreatePasswordAsync(string password, CancellationToken cancellationToken)
    {
        if (_settings.HasPassword)
        {
            throw new BusinessRuleException("A password has been set already: sign in with it.");
        }

        Set(password);
        return Task.CompletedTask;
    }

    public async Task<bool> VerifyAsync(string password, CancellationToken cancellationToken)
    {
        if (Hold is { } hold)
        {
            await hold.Task;
        }

        return _settings.HasPassword
            && PasswordRules.Verify(password, _settings.PasswordHash!, _settings.PasswordSalt!, _settings.PasswordIterations);
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        if (!await VerifyAsync(currentPassword, cancellationToken))
        {
            throw new BusinessRuleException("That isn't the current password.");
        }

        Set(newPassword);
    }

    public Task SetAutoLockMinutesAsync(int minutes, CancellationToken cancellationToken)
    {
        PasswordRules.EnsureValidAutoLock(minutes);
        _settings.AutoLockMinutes = minutes;
        return Task.CompletedTask;
    }

    private void Set(string password)
    {
        var (hash, salt, iterations) = PasswordRules.Hash(password);
        (_settings.PasswordHash, _settings.PasswordSalt, _settings.PasswordIterations) = (hash, salt, iterations);
    }

    private AccessSettings Copy() => new()
    {
        PasswordHash = _settings.PasswordHash,
        PasswordSalt = _settings.PasswordSalt,
        PasswordIterations = _settings.PasswordIterations,
        AutoLockMinutes = _settings.AutoLockMinutes,
    };
}
