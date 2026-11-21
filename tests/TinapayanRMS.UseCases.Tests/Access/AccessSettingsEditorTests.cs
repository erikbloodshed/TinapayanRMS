using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.Access;
using TinapayanRMS.UseCases.Tests.Fixtures;

namespace TinapayanRMS.UseCases.Tests.Access;

/// <summary>
/// The app's password and auto-lock: the first password set and then the one that verifies, a change refused without the
/// current one, and the minutes kept.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class AccessSettingsEditorTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task The_first_password_set_is_the_one_that_verifies()
    {
        var editor = CreateEditor();
        (await editor.GetAsync(Cancellation)).HasPassword.Should().BeFalse();

        await editor.CreatePasswordAsync("pandesal-2026", Cancellation);

        (await editor.GetAsync(Cancellation)).HasPassword.Should().BeTrue();
        (await editor.VerifyAsync("pandesal-2026", Cancellation)).Should().BeTrue();
        (await editor.VerifyAsync("ensaymada-2026", Cancellation)).Should().BeFalse();
    }

    [Test]
    public async Task A_change_without_the_current_password_is_refused_and_keeps_it()
    {
        var editor = CreateEditor();
        await editor.CreatePasswordAsync("pandesal-2026", Cancellation);

        await editor.Awaiting(e => e.ChangePasswordAsync("ensaymada-2026", "monay-2026", Cancellation))
            .Should().ThrowAsync<BusinessRuleException>().WithMessage("That isn't the current password.");
        (await editor.VerifyAsync("pandesal-2026", Cancellation)).Should().BeTrue();

        await editor.ChangePasswordAsync("pandesal-2026", "monay-2026", Cancellation);
        (await editor.VerifyAsync("monay-2026", Cancellation)).Should().BeTrue();
    }

    [Test]
    public async Task The_auto_lock_minutes_are_kept()
    {
        // Not the default, 15, which an empty database reads as already.
        (await CreateEditor().GetAsync(Cancellation)).AutoLockMinutes.Should().Be(Domain.Entities.AccessSettings.DefaultAutoLockMinutes);

        await CreateEditor().SetAutoLockMinutesAsync(30, Cancellation);

        (await CreateEditor().GetAsync(Cancellation)).AutoLockMinutes.Should().Be(30);
    }

    private AccessSettingsEditor CreateEditor() => new(new AccessSettingsRepository(CreateDbContextFactory()));
}
