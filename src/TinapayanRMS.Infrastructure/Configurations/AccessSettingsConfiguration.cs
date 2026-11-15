using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class AccessSettingsConfiguration : IEntityTypeConfiguration<AccessSettings>
{
    public void Configure(EntityTypeBuilder<AccessSettings> builder)
    {
        // A settings record, not a list: the one row always has Id 1, so it's never an identity.
        builder.Property(s => s.Id).ValueGeneratedNever();

        // HasSentinel moves the "leave it to the database" value off 0, so an auto-lock of never is written as set.
        builder.Property(s => s.AutoLockMinutes)
            .HasDefaultValue(AccessSettings.DefaultAutoLockMinutes)
            .HasSentinel(-1);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_AccessSettings_SingleRow", $"id = {AccessSettings.SingletonId}");

            // Backstop for PasswordRules.EnsureValidAutoLock.
            t.HasCheckConstraint(
                "CK_AccessSettings_AutoLockMinutes",
                $"auto_lock_minutes >= 0 AND auto_lock_minutes <= {PasswordRules.MaxAutoLockMinutes}");

            // A password is a hash with its salt and iterations, or none of them (scripts/Reset-Password.ps1 clears both).
            t.HasCheckConstraint(
                "CK_AccessSettings_PasswordWhole",
                "(password_hash IS NULL AND password_salt IS NULL) OR (password_hash IS NOT NULL AND password_salt IS NOT NULL AND password_iterations > 0)");
        });
    }
}
