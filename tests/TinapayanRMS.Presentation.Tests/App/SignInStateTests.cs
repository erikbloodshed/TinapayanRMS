using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.App;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.Access;
using TinapayanRMS.UseCases.Database;

namespace TinapayanRMS.Presentation.Tests.App;

/// <summary>
/// The sign-in panel: it asks for a new password while there's none, the password once there is, unlocks the window with
/// the right one and the auto-lock's minutes, says so for a wrong one, and never lets a database it can't reach in.
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class SignInStateTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private static readonly DateTime Start = new(2026, 11, 15, 8, 0, 0);

    private static readonly string BackupsFolder = Path.Combine(Path.GetTempPath(), "tinapayanrms-sign-in-tests");

    private SignInState CreateState(
        AppLock appLock, IDbContextFactory<TinapayanRMSDbContext>? factory = null, IDatabaseUpdater? updater = null)
    {
        factory ??= CreateDbContextFactory();
        var logger = new LoggerConfiguration().CreateLogger();

        // The real updater over the test database, which the fixture keeps up to date: it finds nothing to do, and backs
        // nothing up. Its backups' folder is only ever made for a backup.
        updater ??= new DatabaseUpdater(factory, new DatabaseBackups(factory, BackupsFolder));
        return new SignInState(new AccessSettingsEditor(new AccessSettingsRepository(factory)), appLock, new DatabaseReadiness(new DatabaseUpdates(updater), logger), logger);
    }

    /// <summary>A database a newer copy of the app has updated.</summary>
    private sealed class NewerDatabase : IDatabaseUpdater
    {
        public int Asked { get; private set; }

        public Task<DatabaseUpdate> UpdateAsync(Action<int>? applying, CancellationToken cancellationToken)
        {
            Asked++;
            throw new BusinessRuleException(BackupRules.DatabaseFromLaterVersionText);
        }
    }

    private AccessSettingsRepository Repository() => new(CreateDbContextFactory());

    [Test]
    public Task With_no_password_it_asks_for_one_twice_and_setting_it_unlocks_the_window() => UiThread.RunAsync(async () =>
    {
        var appLock = new AppLock(new TestClock(Start));
        var state = CreateState(appLock);
        await state.LoadCommand.ExecuteAsync();
        state.IsLoaded.Should().BeTrue();
        state.IsSettingUp.Should().BeTrue();

        state.Password = "pandesal 6am";
        state.Confirmation = "pandesal 6am";
        await state.SetPasswordCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        appLock.IsLocked.Should().BeFalse();
        (state.Password, state.Confirmation).Should().Be(("", ""));
        (await Repository().VerifyAsync("pandesal 6am", Cancellation)).Should().BeTrue();
    });

    [Test]
    public Task A_new_password_typed_differently_twice_or_too_short_is_refused_and_the_window_stays_locked() => UiThread.RunAsync(async () =>
    {
        var appLock = new AppLock(new TestClock(Start));
        var state = CreateState(appLock);
        await state.LoadCommand.ExecuteAsync();

        state.Password = "pandesal 6am";
        state.Confirmation = "pandesal 7am";
        await state.SetPasswordCommand.ExecuteAsync();
        state.ErrorMessage.Should().StartWith("The two passwords aren't the same");

        state.Password = "123";
        state.Confirmation = "123";
        await state.SetPasswordCommand.ExecuteAsync();
        state.ErrorMessage.Should().Be("The password must have at least 6 characters.");

        appLock.IsLocked.Should().BeTrue();
        (await Repository().GetAsync(Cancellation)).HasPassword.Should().BeFalse();
    });

    [Test]
    public Task With_a_password_set_the_right_one_unlocks_with_the_auto_lock_saved() => UiThread.RunAsync(async () =>
    {
        await Repository().CreatePasswordAsync("pandesal 6am", Cancellation);
        await Repository().SetAutoLockMinutesAsync(7, Cancellation);
        var appLock = new AppLock(new TestClock(Start));
        var state = CreateState(appLock);
        await state.LoadCommand.ExecuteAsync();
        state.IsSettingUp.Should().BeFalse();

        state.Password = "pandesal 6am";
        await state.SignInCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        appLock.IsLocked.Should().BeFalse();
        appLock.AutoLockMinutes.Should().Be(7);
        state.Password.Should().BeEmpty();
    });

    [Test]
    public Task A_wrong_password_says_so_and_the_fifth_in_a_row_makes_the_next_wait() => UiThread.RunAsync(async () =>
    {
        await Repository().CreatePasswordAsync("pandesal 6am", Cancellation);
        var appLock = new AppLock(new TestClock(Start));
        var state = CreateState(appLock);
        await state.LoadCommand.ExecuteAsync();

        state.Password = "pandesal 7am";
        await state.SignInCommand.ExecuteAsync();
        state.ErrorMessage.Should().Be("That isn't the password.");
        state.Password.Should().BeEmpty("what's typed is cleared after every try");

        for (var i = 1; i < AppLock.WrongTriesBeforeWait; i++)
        {
            state.Password = "pandesal 7am";
            await state.SignInCommand.ExecuteAsync();
        }

        state.ErrorMessage.Should().Be("That isn't the password. Too many wrong tries: wait 30 seconds, then try again.");

        // Even the right one waits: the clock hasn't moved.
        state.Password = "pandesal 6am";
        await state.SignInCommand.ExecuteAsync();
        state.ErrorMessage.Should().Be("Too many wrong tries: wait 30 seconds, then try again.");
        appLock.IsLocked.Should().BeTrue();
    });

    [Test]
    public Task Nothing_typed_is_asked_for_without_counting_as_a_wrong_try() => UiThread.RunAsync(async () =>
    {
        await Repository().CreatePasswordAsync("pandesal 6am", Cancellation);
        var appLock = new AppLock(new TestClock(Start));
        var state = CreateState(appLock);
        await state.LoadCommand.ExecuteAsync();

        for (var i = 0; i < AppLock.WrongTriesBeforeWait; i++)
        {
            await state.SignInCommand.ExecuteAsync();
        }

        state.ErrorMessage.Should().Be("Type the password.");
        appLock.WaitLeft.Should().Be(TimeSpan.Zero);
    });

    [Test]
    public Task A_password_cleared_while_the_panel_is_up_turns_it_to_setting_a_new_one() => UiThread.RunAsync(async () =>
    {
        await Repository().CreatePasswordAsync("pandesal 6am", Cancellation);
        var appLock = new AppLock(new TestClock(Start));
        var state = CreateState(appLock);
        await state.LoadCommand.ExecuteAsync();

        // scripts/Reset-Password.ps1, run meanwhile.
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE access_settings SET password_hash = NULL, password_salt = NULL, password_changed_at = NULL", Cancellation);
        }

        state.Password = "pandesal 6am";
        await state.SignInCommand.ExecuteAsync();

        state.IsSettingUp.Should().BeTrue();
        state.ErrorMessage.Should().Be("There's no password any more: set a new one.");
        appLock.IsLocked.Should().BeTrue();
    });

    [Test]
    public Task A_password_set_elsewhere_while_setting_one_up_turns_it_to_signing_in() => UiThread.RunAsync(async () =>
    {
        var appLock = new AppLock(new TestClock(Start));
        var state = CreateState(appLock);
        await state.LoadCommand.ExecuteAsync();
        await Repository().CreatePasswordAsync("pandesal 6am", Cancellation);

        state.Password = "ensaymada";
        state.Confirmation = "ensaymada";
        await state.SetPasswordCommand.ExecuteAsync();

        state.IsSettingUp.Should().BeFalse();
        state.ErrorMessage.Should().Be("A password has been set already: sign in with it.");
        appLock.IsLocked.Should().BeTrue();
        (await Repository().VerifyAsync("pandesal 6am", Cancellation)).Should().BeTrue();
    });

    [Test]
    public Task A_database_a_newer_copy_updated_says_so_in_the_panel_never_unlocks_and_is_asked_again_by_Try_Again() => UiThread.RunAsync(async () =>
    {
        var appLock = new AppLock(new TestClock(Start));
        var newer = new NewerDatabase();
        var state = CreateState(appLock, updater: newer);

        await state.LoadCommand.ExecuteAsync();

        state.IsLoaded.Should().BeFalse("the password isn't read from a database this copy can't use");
        state.ErrorMessage.Should().Be(BackupRules.DatabaseFromLaterVersionText);
        appLock.IsLocked.Should().BeTrue();

        await state.LoadCommand.ExecuteAsync();
        newer.Asked.Should().Be(2, "a failed run is run again by the next load");
    });

    [Test]
    public Task A_database_it_cant_reach_says_so_and_never_unlocks() => UiThread.RunAsync(async () =>
    {
        await Repository().CreatePasswordAsync("pandesal 6am", Cancellation);
        var factory = new FailingDbContextFactory(CreateDbContextFactory()) { Fail = true };
        var appLock = new AppLock(new TestClock(Start));
        var state = CreateState(appLock, factory);

        await state.LoadCommand.ExecuteAsync();
        state.IsLoaded.Should().BeFalse();
        state.ErrorMessage.Should().NotBeNullOrEmpty();

        state.Password = "pandesal 6am";
        await state.SignInCommand.ExecuteAsync();
        appLock.IsLocked.Should().BeTrue();

        factory.Fail = false;
        await state.LoadCommand.ExecuteAsync();
        state.IsLoaded.Should().BeTrue();
        state.ErrorMessage.Should().BeNull();
    });
}
