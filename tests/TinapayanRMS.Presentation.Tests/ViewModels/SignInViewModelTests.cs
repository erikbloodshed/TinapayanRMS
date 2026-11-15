using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>
/// The sign-in panel: it asks for a new password while there's none, the password once there is, unlocks the window with
/// the right one and the auto-lock's minutes, says so for a wrong one, and never lets a database it can't reach in.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class SignInViewModelTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private static readonly DateTime Start = new(2026, 11, 15, 8, 0, 0);

    private SignInViewModel CreateViewModel(AppLock appLock, IDbContextFactory<TinapayanRMSDbContext>? factory = null)
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        return new SignInViewModel(
            new AccessSettingsRepository(factory ?? CreateDbContextFactory()), appLock, new LoggerConfiguration().CreateLogger());
    }

    private AccessSettingsRepository Repository() => new(CreateDbContextFactory());

    [Test]
    public Task With_no_password_it_asks_for_one_twice_and_setting_it_unlocks_the_window() => UiThread.RunAsync(async () =>
    {
        var appLock = new AppLock(new TestClock(Start));
        var viewModel = CreateViewModel(appLock);
        await viewModel.LoadCommand.Execute();
        viewModel.IsLoaded.Should().BeTrue();
        viewModel.IsSettingUp.Should().BeTrue();

        viewModel.Password = "pandesal 6am";
        viewModel.Confirmation = "pandesal 6am";
        await viewModel.SetPasswordCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        appLock.IsLocked.Should().BeFalse();
        (viewModel.Password, viewModel.Confirmation).Should().Be(("", ""));
        (await Repository().VerifyAsync("pandesal 6am", Cancellation)).Should().BeTrue();
    });

    [Test]
    public Task A_new_password_typed_differently_twice_or_too_short_is_refused_and_the_window_stays_locked() => UiThread.RunAsync(async () =>
    {
        var appLock = new AppLock(new TestClock(Start));
        var viewModel = CreateViewModel(appLock);
        await viewModel.LoadCommand.Execute();

        viewModel.Password = "pandesal 6am";
        viewModel.Confirmation = "pandesal 7am";
        await viewModel.SetPasswordCommand.Execute();
        viewModel.ErrorMessage.Should().StartWith("The two passwords aren't the same");

        viewModel.Password = "123";
        viewModel.Confirmation = "123";
        await viewModel.SetPasswordCommand.Execute();
        viewModel.ErrorMessage.Should().Be("The password must have at least 6 characters.");

        appLock.IsLocked.Should().BeTrue();
        (await Repository().GetAsync(Cancellation)).HasPassword.Should().BeFalse();
    });

    [Test]
    public Task With_a_password_set_the_right_one_unlocks_with_the_auto_lock_saved() => UiThread.RunAsync(async () =>
    {
        await Repository().CreatePasswordAsync("pandesal 6am", Cancellation);
        await Repository().SetAutoLockMinutesAsync(7, Cancellation);
        var appLock = new AppLock(new TestClock(Start));
        var viewModel = CreateViewModel(appLock);
        await viewModel.LoadCommand.Execute();
        viewModel.IsSettingUp.Should().BeFalse();

        viewModel.Password = "pandesal 6am";
        await viewModel.SignInCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        appLock.IsLocked.Should().BeFalse();
        appLock.AutoLockMinutes.Should().Be(7);
        viewModel.Password.Should().BeEmpty();
    });

    [Test]
    public Task A_wrong_password_says_so_and_the_fifth_in_a_row_makes_the_next_wait() => UiThread.RunAsync(async () =>
    {
        await Repository().CreatePasswordAsync("pandesal 6am", Cancellation);
        var appLock = new AppLock(new TestClock(Start));
        var viewModel = CreateViewModel(appLock);
        await viewModel.LoadCommand.Execute();

        viewModel.Password = "pandesal 7am";
        await viewModel.SignInCommand.Execute();
        viewModel.ErrorMessage.Should().Be("That isn't the password.");
        viewModel.Password.Should().BeEmpty("what's typed is cleared after every try");

        for (var i = 1; i < AppLock.WrongTriesBeforeWait; i++)
        {
            viewModel.Password = "pandesal 7am";
            await viewModel.SignInCommand.Execute();
        }

        viewModel.ErrorMessage.Should().Be("That isn't the password. Too many wrong tries: wait 30 seconds, then try again.");

        // Even the right one waits: the clock hasn't moved.
        viewModel.Password = "pandesal 6am";
        await viewModel.SignInCommand.Execute();
        viewModel.ErrorMessage.Should().Be("Too many wrong tries: wait 30 seconds, then try again.");
        appLock.IsLocked.Should().BeTrue();
    });

    [Test]
    public Task Nothing_typed_is_asked_for_without_counting_as_a_wrong_try() => UiThread.RunAsync(async () =>
    {
        await Repository().CreatePasswordAsync("pandesal 6am", Cancellation);
        var appLock = new AppLock(new TestClock(Start));
        var viewModel = CreateViewModel(appLock);
        await viewModel.LoadCommand.Execute();

        for (var i = 0; i < AppLock.WrongTriesBeforeWait; i++)
        {
            await viewModel.SignInCommand.Execute();
        }

        viewModel.ErrorMessage.Should().Be("Type the password.");
        appLock.WaitLeft.Should().Be(TimeSpan.Zero);
    });

    [Test]
    public Task A_password_cleared_while_the_panel_is_up_turns_it_to_setting_a_new_one() => UiThread.RunAsync(async () =>
    {
        await Repository().CreatePasswordAsync("pandesal 6am", Cancellation);
        var appLock = new AppLock(new TestClock(Start));
        var viewModel = CreateViewModel(appLock);
        await viewModel.LoadCommand.Execute();

        // scripts/Reset-Password.ps1, run meanwhile.
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE access_settings SET password_hash = NULL, password_salt = NULL, password_changed_at = NULL", Cancellation);
        }

        viewModel.Password = "pandesal 6am";
        await viewModel.SignInCommand.Execute();

        viewModel.IsSettingUp.Should().BeTrue();
        viewModel.ErrorMessage.Should().Be("There's no password any more: set a new one.");
        appLock.IsLocked.Should().BeTrue();
    });

    [Test]
    public Task A_password_set_elsewhere_while_setting_one_up_turns_it_to_signing_in() => UiThread.RunAsync(async () =>
    {
        var appLock = new AppLock(new TestClock(Start));
        var viewModel = CreateViewModel(appLock);
        await viewModel.LoadCommand.Execute();
        await Repository().CreatePasswordAsync("pandesal 6am", Cancellation);

        viewModel.Password = "ensaymada";
        viewModel.Confirmation = "ensaymada";
        await viewModel.SetPasswordCommand.Execute();

        viewModel.IsSettingUp.Should().BeFalse();
        viewModel.ErrorMessage.Should().Be("A password has been set already: sign in with it.");
        appLock.IsLocked.Should().BeTrue();
        (await Repository().VerifyAsync("pandesal 6am", Cancellation)).Should().BeTrue();
    });

    [Test]
    public Task A_database_it_cant_reach_says_so_and_never_unlocks() => UiThread.RunAsync(async () =>
    {
        await Repository().CreatePasswordAsync("pandesal 6am", Cancellation);
        var factory = new FailingDbContextFactory(CreateDbContextFactory()) { Fail = true };
        var appLock = new AppLock(new TestClock(Start));
        var viewModel = CreateViewModel(appLock, factory);

        await viewModel.LoadCommand.Execute();
        viewModel.IsLoaded.Should().BeFalse();
        viewModel.ErrorMessage.Should().NotBeNullOrEmpty();

        viewModel.Password = "pandesal 6am";
        await viewModel.SignInCommand.Execute();
        appLock.IsLocked.Should().BeTrue();

        factory.Fail = false;
        await viewModel.LoadCommand.Execute();
        viewModel.IsLoaded.Should().BeTrue();
        viewModel.ErrorMessage.Should().BeNull();
    });
}
