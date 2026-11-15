using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public sealed class AccessSettingsRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IAccessSettingsRepository
{
    public async Task<AccessSettings> GetAsync(CancellationToken cancellationToken) =>
        await dbContextFactory.QuerySingleOrDefaultAsync<AccessSettings>(
            $"SELECT {Columns.Of<AccessSettings>("s")} FROM access_settings s WHERE s.id = @id",
            new { id = AccessSettings.SingletonId },
            cancellationToken)
        ?? new AccessSettings();

    public async Task CreatePasswordAsync(string password, CancellationToken cancellationToken)
    {
        PasswordRules.EnsureValid(password);
        var hashed = await Task.Run(() => PasswordRules.Hash(password), cancellationToken);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await FindAsync(dbContext, cancellationToken);
        if (settings.HasPassword)
        {
            throw new BusinessRuleException("A password has been set already: sign in with it.");
        }

        SetPassword(dbContext, settings, hashed);
        // Two first saves at once can't both land: the second's insert of row 1 is refused by its key.
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> VerifyAsync(string password, CancellationToken cancellationToken)
    {
        var settings = await GetAsync(cancellationToken);
        return settings.HasPassword && await Task.Run(
            () => PasswordRules.Verify(password, settings.PasswordHash!, settings.PasswordSalt!, settings.PasswordIterations),
            cancellationToken);
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        PasswordRules.EnsureValid(newPassword);
        if (!await VerifyAsync(currentPassword, cancellationToken))
        {
            throw new BusinessRuleException("That isn't the current password.");
        }

        var hashed = await Task.Run(() => PasswordRules.Hash(newPassword), cancellationToken);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await FindAsync(dbContext, cancellationToken);
        SetPassword(dbContext, settings, hashed);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetAutoLockMinutesAsync(int minutes, CancellationToken cancellationToken)
    {
        PasswordRules.EnsureValidAutoLock(minutes);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await FindAsync(dbContext, cancellationToken);
        settings.AutoLockMinutes = minutes;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The tracked row, or a new one added to the context to be saved.</summary>
    private static async Task<AccessSettings> FindAsync(TinapayanRMSDbContext dbContext, CancellationToken cancellationToken)
    {
        var settings = await dbContext.AccessSettings.FirstOrDefaultAsync(s => s.Id == AccessSettings.SingletonId, cancellationToken);
        if (settings is null)
        {
            settings = new AccessSettings();
            dbContext.AccessSettings.Add(settings);
        }

        return settings;
    }

    private static void SetPassword(TinapayanRMSDbContext dbContext, AccessSettings settings, (byte[] Hash, byte[] Salt, int Iterations) hashed)
    {
        settings.PasswordHash = hashed.Hash;
        settings.PasswordSalt = hashed.Salt;
        settings.PasswordIterations = hashed.Iterations;
        settings.PasswordChangedAt = dbContext.LocalNow();
    }
}
