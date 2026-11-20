using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// What the window asks before it closes. Closing a window isn't a move to another address, and no move asks anything
/// anyway: every form that can hold unsaved changes is a record view's tab, which stays as it is while another screen
/// shows. Two things keep it open: work that closing would cut short (<see cref="IBusyWork"/>: a backup or restore running
/// in Settings → Database), which the window waits for, saying why; and a form's unsaved changes, asked about as closing
/// its tab asks. The forms are the record views' (<see cref="IOpenForms"/>), every tab's, whether a page shows the view or
/// not: a screen that failed and gave way to its error panel (ScreenErrorBoundary) still has its view, and the view its
/// forms. One per page the window loads: the newest page's is the one <see cref="AppStatus.KeepsWindowOpen"/> asks, for
/// Windows.
/// </summary>
public sealed class UnsavedChangesGuard : IDisposable
{
    /// <summary>What Windows shows beside the app while it keeps a sign-out, a shutdown or an install waiting for it.</summary>
    public const string UnsavedChangesReason = "A product, sub-assembly or raw material has changes you haven't saved.";

    private readonly IServiceProvider _services;
    private readonly Notices _notices;
    private readonly Confirmations _confirmations;
    private readonly AppStatus _app;

    /// <summary>
    /// The page's record views, found the first time they're asked about: the same ones as long as the page lasts. Every
    /// screen asks after each drawing (its leave warning), which fetched them from the services each time.
    /// </summary>
    private IOpenForms[]? _views;

    /// <param name="services">The page's services, for the screens that may be busy and the record views' open forms.</param>
    /// <param name="notices">The page's notices, to say why the window stays open.</param>
    /// <param name="confirmations">The page's questions, which ask about a form whose own screen isn't showing it.</param>
    /// <param name="app">The app's status, which follows this guard while its page is the newest.</param>
    public UnsavedChangesGuard(IServiceProvider services, Notices notices, Confirmations confirmations, AppStatus app)
    {
        _services = services;
        _notices = notices;
        _confirmations = confirmations;
        _app = app;
        app.Follow(this);
    }

    /// <summary>Whether a record view's form, in any tab, holds anything a Save would write.</summary>
    public bool HasUnsavedChanges
    {
        get
        {
            foreach (var view in Views)
            {
                foreach (var form in view.OpenForms)
                {
                    if (form.HasUnsavedChanges)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Why the window mustn't close without asking, in words for the user, found at once and asking nothing: what's
    /// running that closing would cut short, or that a form holds unsaved changes; null when it may close. For Windows'
    /// question before it ends the app, which wants its answer there and then (<see cref="AppStatus.KeepsWindowOpen"/>).
    /// </summary>
    public string? KeepsWindowOpen => BusyWith() ?? (HasUnsavedChanges ? UnsavedChangesReason : null);

    /// <summary>
    /// True when the window may close: nothing is running that closing would cut short, and every form with unsaved
    /// changes had them saved, or the user chose not to save. While something is running, the notices say what, and the
    /// window stays open; Cancel on a form keeps it open too.
    /// </summary>
    public async Task<bool> ResolveBeforeClosingAsync()
    {
        if (BusyWith() is { } why)
        {
            _notices.ShowError(why);
            return false;
        }

        foreach (var form in Views.SelectMany(view => view.OpenForms).Where(form => form.HasUnsavedChanges).ToList())
        {
            if (!await ResolveAsync(form))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Asks about <paramref name="form"/>'s unsaved changes, answering its questions here as its screen would: a record view's
    /// form in a tab not showing, or in a view not showing, has no page to answer them. A Save refused says why here too.
    /// </summary>
    private async Task<bool> ResolveAsync(IUnsavedChangesScreen form)
    {
        var screen = form as ScreenState;
        using var unsaved = screen?.ConfirmUnsavedChanges.RegisterHandler(async question =>
            question.SetOutput(await _confirmations.AskAboutUnsavedChangesAsync(question.Input)));
        using var confirm = screen?.ConfirmDelete.RegisterHandler(async question =>
            question.SetOutput(await _confirmations.AskAsync(question.Input)));
        if (await form.ResolveUnsavedChangesAsync(revert: false))
        {
            return true;
        }

        if (screen?.ErrorMessage is { } why)
        {
            _notices.ShowError(why);
        }

        return false;
    }

    /// <summary>The app's status lets go of this guard as its page goes (the scope ends).</summary>
    public void Dispose() => _app.Forget(this);

    private IOpenForms[] Views => _views ??= [.. _services.GetServices<IOpenForms>()];

    /// <summary>Why something running keeps the window open (<see cref="IBusyWork"/>); null while nothing does.</summary>
    private string? BusyWith() =>
        _services.GetServices<IBusyWork>().Select(work => work.KeepsWindowOpen).FirstOrDefault(why => why is not null);
}
