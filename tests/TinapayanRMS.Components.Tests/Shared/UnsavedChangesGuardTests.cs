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
/// The forms it asks about are the record views' tabs (IOpenForms), whether a page shows them or not.
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

    /// <summary>A record view's tabs, as the guard finds them: the forms open in it, one a tab.</summary>
    private sealed class OpenForms : IOpenForms
    {
        public List<Form> Forms { get; } = [];

        IReadOnlyList<IUnsavedChangesScreen> IOpenForms.OpenForms => Forms;
    }

    private static OpenForms AddView(BunitContext context)
    {
        context.Services.AddScoped<OpenForms>();
        context.Services.AddScoped<IOpenForms>(provider => provider.GetRequiredService<OpenForms>());
        return context.Services.GetRequiredService<OpenForms>();
    }

    [Test]
    public async Task Each_form_with_unsaved_changes_is_asked_about_as_the_window_closes()
    {
        await using var context = AppServices.NewContext();
        var view = AddView(context);
        var edited = new Form { HasUnsavedChanges = true };
        var untouched = new Form();
        view.Forms.AddRange([untouched, edited]);
        var guard = context.Services.GetRequiredService<UnsavedChangesGuard>();

        guard.HasUnsavedChanges.Should().BeTrue();
        (await guard.ResolveBeforeClosingAsync()).Should().BeTrue();
        edited.Asked.Should().Be(1);
        untouched.Asked.Should().Be(0, "it has nothing to save");
    }

    // A tab opened after the window first asked is asked about too: the guard keeps the views, not their forms.
    [Test]
    public async Task A_tab_opened_since_the_last_question_is_asked_about_too()
    {
        await using var context = AppServices.NewContext();
        var view = AddView(context);
        var guard = context.Services.GetRequiredService<UnsavedChangesGuard>();
        guard.HasUnsavedChanges.Should().BeFalse();

        var opened = new Form { HasUnsavedChanges = true };
        view.Forms.Add(opened);

        guard.HasUnsavedChanges.Should().BeTrue();
        (await guard.ResolveBeforeClosingAsync()).Should().BeTrue();
        opened.Asked.Should().Be(1);
    }

    // Windows asks before a sign-out, a shutdown or an install ends the app, and wants its answer there and then
    // (WindowsWindow): what keeps the window open is told at once, and nothing is asked of the user for it.
    [Test]
    public async Task What_keeps_the_window_open_is_told_at_once_without_asking()
    {
        await using var context = AppServices.NewContext();
        var busy = new Busy();
        context.Services.AddScoped<IBusyWork>(_ => busy);
        var view = AddView(context);
        var app = context.Services.GetRequiredService<AppStatus>();
        app.KeepsWindowOpen.Should().BeNull("no screen is up yet");

        context.Services.GetRequiredService<UnsavedChangesGuard>();
        var form = new Form();
        view.Forms.Add(form);
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
        context.Services.AddScoped<OpenForms>();
        context.Services.AddScoped<IOpenForms>(provider => provider.GetRequiredService<OpenForms>());
        var app = context.Services.GetRequiredService<AppStatus>();
        var firstPage = context.Services.CreateScope();
        firstPage.ServiceProvider.GetRequiredService<UnsavedChangesGuard>();
        firstPage.ServiceProvider.GetRequiredService<OpenForms>().Forms.Add(new Form { HasUnsavedChanges = true });
        app.KeepsWindowOpen.Should().NotBeNull();

        using (var reloaded = context.Services.CreateScope())
        {
            reloaded.ServiceProvider.GetRequiredService<UnsavedChangesGuard>();
            app.KeepsWindowOpen.Should().BeNull("the reloaded page has nothing unsaved");

            firstPage.Dispose();
            reloaded.ServiceProvider.GetRequiredService<OpenForms>().Forms.Add(new Form { HasUnsavedChanges = true });
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
