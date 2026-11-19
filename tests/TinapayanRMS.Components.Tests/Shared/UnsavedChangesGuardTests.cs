using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.Settings;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.SubAssemblies;

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
    // would have asked: what the form held is still its state's, so the window still asks about it as it closes.
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

    // Windows asks before a sign-out, a shutdown or an install ends the app, and wants its answer there and then
    // (WindowsWindow): what keeps the window open is told at once, and nothing is asked of the user for it.
    [Test]
    public async Task What_keeps_the_window_open_is_told_at_once_without_asking()
    {
        await using var context = AppServices.NewContext();
        var busy = new Busy();
        context.Services.AddScoped<IBusyWork>(_ => busy);
        var app = context.Services.GetRequiredService<AppStatus>();
        app.KeepsWindowOpen.Should().BeNull("no screen is up yet");

        var guard = context.Services.GetRequiredService<UnsavedChangesGuard>();
        var form = new Form();
        using var watching = guard.Watch(form);
        app.KeepsWindowOpen.Should().BeNull("the form has nothing unsaved");

        form.HasUnsavedChanges = true;
        app.KeepsWindowOpen.Should().Be("A product, sub-assembly or raw material has changes you haven't saved.");

        busy.KeepsWindowOpen = "A restore is running, and closing now would cut it short.";
        app.KeepsWindowOpen.Should().Be("A restore is running, and closing now would cut it short.");
        form.Asked.Should().Be(0);
    }

    // A restore reloads the page, which brings a guard of its own: the window asks that one from then on, and the old
    // page's going doesn't take it away.
    [Test]
    public async Task The_newest_pages_guard_answers_for_the_window()
    {
        await using var context = AppServices.NewContext();
        var app = context.Services.GetRequiredService<AppStatus>();
        var firstPage = context.Services.CreateScope();
        using var firstForm = firstPage.ServiceProvider.GetRequiredService<UnsavedChangesGuard>().Watch(new Form { HasUnsavedChanges = true });
        app.KeepsWindowOpen.Should().NotBeNull();

        using (var reloaded = context.Services.CreateScope())
        {
            var guard = reloaded.ServiceProvider.GetRequiredService<UnsavedChangesGuard>();
            app.KeepsWindowOpen.Should().BeNull("the reloaded page has nothing unsaved");

            firstPage.Dispose();
            using var reloadedForm = guard.Watch(new Form { HasUnsavedChanges = true });
            app.KeepsWindowOpen.Should().NotBeNull("the old page went, and the reloaded one's guard still answers");
        }

        app.KeepsWindowOpen.Should().BeNull("no page is loaded");
    }

    // A record view's tab keeps its form while another tab, or another screen, shows: no page shows it, so the window asks
    // about it itself as it closes, and Cancel keeps the window open.
    [Test]
    public async Task A_record_views_tab_with_changes_not_showing_is_asked_about_as_the_window_closes()
    {
        await using var context = AppServices.NewContext();
        var guard = context.Services.GetRequiredService<UnsavedChangesGuard>();
        var confirmations = context.Services.GetRequiredService<Confirmations>();
        var view = context.Services.GetRequiredService<SubAssembliesState>();
        var form = view.Form!;
        guard.HasUnsavedChanges.Should().BeFalse("the view opens on a blank form");

        form.EditingName = "Sweet Dough";
        await view.NewCommand.ExecuteAsync();
        view.Form.Should().NotBeSameAs(form, "the one with changes stays in its own tab");
        guard.HasUnsavedChanges.Should().BeTrue();

        var closing = guard.ResolveBeforeClosingAsync();
        confirmations.Question.Should().StartWith("Save the changes to Sweet Dough?");
        confirmations.Answer(UnsavedChangesAnswer.Cancel);

        (await closing).Should().BeFalse("Cancel keeps the window open");
        form.EditingName.Should().Be("Sweet Dough");
    }

    [Test]
    public async Task Settings_Database_keeps_the_window_open_only_while_it_works()
    {
        await using var context = AppServices.NewContext();

        context.Services.GetServices<IBusyWork>().Should().ContainSingle().Which.Should().BeOfType<DatabaseBackupsState>()
            .Which.KeepsWindowOpen.Should().BeNull("it isn't backing up or restoring");
        (await context.Services.GetRequiredService<UnsavedChangesGuard>().ResolveBeforeClosingAsync()).Should().BeTrue();
    }
}
