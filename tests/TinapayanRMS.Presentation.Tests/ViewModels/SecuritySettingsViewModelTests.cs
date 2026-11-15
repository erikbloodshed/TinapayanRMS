using System.Reactive.Linq;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>Settings' Security tab: changes the password with the current one, sets one when there's none, and saves the auto-lock.</summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class SecuritySettingsViewModelTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private readonly AppLock _appLock = new(new TestClock(new DateTime(2026, 11, 15, 8, 0, 0)));

    private SecuritySettingsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        return new SecuritySettingsViewModel(new AccessSettingsRepository(CreateDbContextFactory()), _appLock, new LoggerConfiguration().CreateLogger());
    }

    private AccessSettingsRepository Repository() => new(CreateDbContextFactory());

    [Test]
    public Task The_password_is_changed_with_the_current_one_and_the_boxes_are_cleared() => UiThread.RunAsync(async () =>
    {
        await Repository().CreatePasswordAsync("pandesal 6am", Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.HasPassword.Should().BeTrue();

        viewModel.CurrentPassword = "pandesal 6am";
        viewModel.NewPassword = "ensaymada";
        viewModel.Confirmation = "ensaymada";
        await viewModel.ChangePasswordCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("The password was changed: use the new one from now on.");
        (viewModel.CurrentPassword, viewModel.NewPassword, viewModel.Confirmation).Should().Be(("", "", ""));
        (await Repository().VerifyAsync("ensaymada", Cancellation)).Should().BeTrue();
    });

    [Test]
    public Task A_wrong_current_password_or_a_mismatched_new_one_changes_nothing() => UiThread.RunAsync(async () =>
    {
        await Repository().CreatePasswordAsync("pandesal 6am", Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.CurrentPassword = "pandesal 7am";
        viewModel.NewPassword = "ensaymada";
        viewModel.Confirmation = "ensaymada";
        await viewModel.ChangePasswordCommand.Execute();
        viewModel.ErrorMessage.Should().Be("That isn't the current password.");

        viewModel.CurrentPassword = "pandesal 6am";
        viewModel.NewPassword = "ensaymada";
        viewModel.Confirmation = "ensaymadas";
        await viewModel.ChangePasswordCommand.Execute();
        viewModel.ErrorMessage.Should().StartWith("The two passwords aren't the same");

        (await Repository().VerifyAsync("pandesal 6am", Cancellation)).Should().BeTrue();
    });

    [Test]
    public Task With_no_password_it_sets_one_without_a_current_one() => UiThread.RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.HasPassword.Should().BeFalse();

        viewModel.NewPassword = "ensaymada";
        viewModel.Confirmation = "ensaymada";
        await viewModel.ChangePasswordCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.HasPassword.Should().BeTrue();
        (await Repository().VerifyAsync("ensaymada", Cancellation)).Should().BeTrue();
    });

    [Test]
    public Task The_auto_lock_is_saved_and_takes_effect_at_once() => UiThread.RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.EditingAutoLockMinutes.Should().Be(15);

        viewModel.EditingAutoLockMinutes = 3;
        await viewModel.SaveAutoLockCommand.Execute();

        viewModel.SuccessMessage.Should().Be("Auto-lock saved: the window locks after 3 minutes without a key, click or scroll.");
        _appLock.AutoLockMinutes.Should().Be(3);
        (await Repository().GetAsync(Cancellation)).AutoLockMinutes.Should().Be(3);

        viewModel.EditingAutoLockMinutes = 241;
        await viewModel.SaveAutoLockCommand.Execute();
        viewModel.ErrorMessage.Should().Be("The auto-lock must be from 0 (never) to 240 minutes.");
        _appLock.AutoLockMinutes.Should().Be(3);
    });
}
