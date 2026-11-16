using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.Settings;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// UnsavedChangesGuard, as the window closes: work that closing would cut short keeps it open, and the page's notices say
/// why. The window ends the process as it closes, and a restore cut short could leave the database under another name.
/// </summary>
public class UnsavedChangesGuardTests
{
    private sealed class Busy : IBusyWork
    {
        public string? KeepsWindowOpen { get; set; }
    }

    [Test]
    public async Task The_window_stays_open_while_a_backup_or_restore_runs_and_says_why()
    {
        await using var context = AppServices.NewContext();
        var busy = new Busy { KeepsWindowOpen = "A restore is running, and closing now would cut it short." };
        context.Services.AddScoped<IBusyWork>(_ => busy);
        var shown = new List<(NoticeKind Kind, string? Message)>();
        context.Services.GetRequiredService<Notices>().Changed += (kind, message) => shown.Add((kind, message));
        var guard = context.Services.GetRequiredService<UnsavedChangesGuard>();

        (await guard.ResolveBeforeClosingAsync()).Should().BeFalse();
        shown.Should().Equal((NoticeKind.Error, "A restore is running, and closing now would cut it short."));

        busy.KeepsWindowOpen = null;
        (await guard.ResolveBeforeClosingAsync()).Should().BeTrue("nothing is running now, and no form is unsaved");
    }

    /// <summary>A form, asked about its unsaved changes as the window closes.</summary>
    private sealed class Form : IUnsavedChangesScreen
    {
        public bool HasUnsavedChanges { get; set; }

        public int Asked { get; private set; }

        public Task<bool> ResolveUnsavedChangesAsync(bool revert)
        {
            Asked++;
            HasUnsavedChanges = false;
            return Task.FromResult(true);
        }
    }

    // A screen that failed gives way to its error panel, which takes the screen away without the question leaving it
    // would have asked: what the form held is still its ViewModel's, so the window still asks about it as it closes.
    [Test]
    public async Task A_form_whose_screen_went_with_unsaved_changes_is_still_asked_about_as_the_window_closes()
    {
        await using var context = AppServices.NewContext();
        var guard = context.Services.GetRequiredService<UnsavedChangesGuard>();
        var form = new Form { HasUnsavedChanges = true };

        guard.Watch(form).Dispose();

        guard.HasUnsavedChanges.Should().BeTrue();
        (await guard.ResolveBeforeClosingAsync()).Should().BeTrue();
        form.Asked.Should().Be(1);
    }

    [Test]
    public async Task A_form_whose_screen_went_with_nothing_unsaved_is_let_go_of()
    {
        await using var context = AppServices.NewContext();
        var guard = context.Services.GetRequiredService<UnsavedChangesGuard>();
        var form = new Form();

        guard.Watch(form).Dispose();
        form.HasUnsavedChanges = true;

        (await guard.ResolveBeforeClosingAsync()).Should().BeTrue();
        form.Asked.Should().Be(0, "a screen showing it would watch it again");
    }

    // Products and Print show the same form: the one going leaves it watched by the other.
    [Test]
    public async Task A_form_two_screens_show_stays_watched_while_one_of_them_shows_it()
    {
        await using var context = AppServices.NewContext();
        var guard = context.Services.GetRequiredService<UnsavedChangesGuard>();
        var form = new Form();

        var print = guard.Watch(form);
        guard.Watch(form).Dispose();
        form.HasUnsavedChanges = true;

        (await guard.ResolveBeforeClosingAsync()).Should().BeTrue();
        form.Asked.Should().Be(1);
        print.Dispose();
    }

    [Test]
    public async Task Settings_Database_keeps_the_window_open_only_while_it_works()
    {
        await using var context = AppServices.NewContext();

        context.Services.GetServices<IBusyWork>().Should().ContainSingle().Which.Should().BeOfType<DatabaseBackupsViewModel>()
            .Which.KeepsWindowOpen.Should().BeNull("it isn't backing up or restoring");
        (await context.Services.GetRequiredService<UnsavedChangesGuard>().ResolveBeforeClosingAsync()).Should().BeTrue();
    }
}
