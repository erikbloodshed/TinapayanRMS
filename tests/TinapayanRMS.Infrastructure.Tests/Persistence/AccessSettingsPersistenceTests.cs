using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// The app's one password and its auto-lock: no row reads as no password and a 15-minute auto-lock; the first password is
/// set once, and changed only with the current one; only a salted hash is kept.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class AccessSettingsPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private AccessSettingsRepository CreateRepository() => new(CreateDbContextFactory());

    [Test]
    public async Task Before_anything_is_saved_there_is_no_password_and_the_auto_lock_is_15_minutes()
    {
        var settings = await CreateRepository().GetAsync(Cancellation);

        settings.HasPassword.Should().BeFalse();
        settings.AutoLockMinutes.Should().Be(15);
        (await CreateRepository().VerifyAsync("anything", Cancellation)).Should().BeFalse("there's no password to match");
    }

    [Test]
    public async Task The_first_password_is_kept_as_a_salted_hash_and_verifies()
    {
        await CreateRepository().CreatePasswordAsync("pandesal 6am", Cancellation);

        var settings = await CreateRepository().GetAsync(Cancellation);
        settings.HasPassword.Should().BeTrue();
        settings.PasswordIterations.Should().Be(PasswordRules.Iterations);
        settings.PasswordChangedAt.Should().NotBeNull();
        settings.PasswordHash.Should().NotEqual(System.Text.Encoding.UTF8.GetBytes("pandesal 6am"));
        (await CreateRepository().VerifyAsync("pandesal 6am", Cancellation)).Should().BeTrue();
        (await CreateRepository().VerifyAsync("pandesal 7am", Cancellation)).Should().BeFalse();
    }

    [Test]
    public async Task A_first_password_is_refused_once_one_is_set()
    {
        await CreateRepository().CreatePasswordAsync("pandesal 6am", Cancellation);

        var act = () => CreateRepository().CreatePasswordAsync("ensaymada", Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("A password has been set already: sign in with it.");
        (await CreateRepository().VerifyAsync("pandesal 6am", Cancellation)).Should().BeTrue();
    }

    [Test]
    public async Task A_password_too_short_is_refused_and_nothing_is_saved()
    {
        var act = () => CreateRepository().CreatePasswordAsync("123", Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
        (await CreateRepository().GetAsync(Cancellation)).HasPassword.Should().BeFalse();
    }

    [Test]
    public async Task The_password_is_changed_with_the_current_one_and_refused_with_a_wrong_one()
    {
        await CreateRepository().CreatePasswordAsync("pandesal 6am", Cancellation);

        var wrong = () => CreateRepository().ChangePasswordAsync("pandesal 7am", "ensaymada", Cancellation);
        await wrong.Should().ThrowAsync<BusinessRuleException>().WithMessage("That isn't the current password.");
        (await CreateRepository().VerifyAsync("pandesal 6am", Cancellation)).Should().BeTrue();

        await CreateRepository().ChangePasswordAsync("pandesal 6am", "ensaymada", Cancellation);
        (await CreateRepository().VerifyAsync("ensaymada", Cancellation)).Should().BeTrue();
        (await CreateRepository().VerifyAsync("pandesal 6am", Cancellation)).Should().BeFalse();
    }

    [Test]
    public async Task The_auto_lock_keeps_what_was_set_even_never_and_keeps_the_password()
    {
        await CreateRepository().SetAutoLockMinutesAsync(0, Cancellation);
        (await CreateRepository().GetAsync(Cancellation)).AutoLockMinutes.Should().Be(0, "0 isn't left for the store default to make 15");

        await CreateRepository().CreatePasswordAsync("pandesal 6am", Cancellation);
        await CreateRepository().SetAutoLockMinutesAsync(5, Cancellation);

        var settings = await CreateRepository().GetAsync(Cancellation);
        settings.AutoLockMinutes.Should().Be(5);
        (await CreateRepository().VerifyAsync("pandesal 6am", Cancellation)).Should().BeTrue();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(241)]
    public async Task An_auto_lock_outside_0_to_240_minutes_is_refused(int minutes)
    {
        var act = () => CreateRepository().SetAutoLockMinutesAsync(minutes, Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
    public async Task The_database_refuses_a_second_row_a_hash_without_its_salt_and_an_auto_lock_out_of_range()
    {
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AccessSettings.Add(new AccessSettings { Id = 2 });
            var act = () => dbContext.SaveChangesAsync(Cancellation);
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        await using (var dbContext = CreateDbContext())
        {
            dbContext.AccessSettings.Add(new AccessSettings { PasswordHash = [1, 2, 3], PasswordIterations = 1 });
            var act = () => dbContext.SaveChangesAsync(Cancellation);
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        await using (var dbContext = CreateDbContext())
        {
            dbContext.AccessSettings.Add(new AccessSettings { AutoLockMinutes = 241 });
            var act = () => dbContext.SaveChangesAsync(Cancellation);
            await act.Should().ThrowAsync<DbUpdateException>();
        }
    }

    [Test]
    public async Task The_reset_scripts_update_leaves_no_password_and_the_auto_lock_as_it_was()
    {
        await CreateRepository().CreatePasswordAsync("pandesal 6am", Cancellation);
        await CreateRepository().SetAutoLockMinutesAsync(5, Cancellation);

        // What scripts/Reset-Password.ps1 runs.
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE access_settings SET password_hash = NULL, password_salt = NULL, password_changed_at = NULL", Cancellation);
        }

        var settings = await CreateRepository().GetAsync(Cancellation);
        settings.HasPassword.Should().BeFalse();
        settings.AutoLockMinutes.Should().Be(5);
        await CreateRepository().CreatePasswordAsync("ensaymada", Cancellation);
        (await CreateRepository().VerifyAsync("ensaymada", Cancellation)).Should().BeTrue();
    }
}
