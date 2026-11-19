using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// What the window asks before it closes. Closing a window isn't a move to another address, so
/// ScreenBase's own question never sees it. Two things keep it open: work that closing would cut short (<see cref="IBusyWork"/>:
/// a backup or restore running in Settings → Database), which the window waits for, saying why; and a form's unsaved
/// changes, asked about as leaving the form asks. ScreenBase keeps its form here while its screen is up. A form whose
/// screen went while it still held unsaved changes is kept too: a screen that failed gives way to its error panel
/// (ScreenErrorBoundary) without the question leaving it would have asked, and what it held is still its state's. A record
/// view's forms are asked of the view (<see cref="IOpenForms"/>), every tab's, whether the view shows or not. One per page
/// the window loads: the newest page's is the one <see cref="AppStatus.KeepsWindowOpen"/> asks, for Windows.
/// </summary>
public sealed class UnsavedChangesGuard : IDisposable
{
    /// <summary>What Windows shows beside the app while it keeps a sign-out, a shutdown or an install waiting for it.</summary>
    public const string UnsavedChangesReason = "A product, sub-assembly or raw material has changes you haven't saved.";

    private readonly IServiceProvider _services;
    private readonly Notices _notices;
    private readonly Confirmations _confirmations;
    private readonly AppStatus _app;

    /// <summary>The forms on show, by how many screens show each (Products and Print show the same one).</summary>
    private readonly Dictionary<IUnsavedChangesScreen, int> _shown = new(ReferenceEqualityComparer.Instance);

    /// <summary>Forms whose screens went while they held unsaved changes.</summary>
    private readonly HashSet<IUnsavedChangesScreen> _leftUnsaved = new(ReferenceEqualityComparer.Instance);

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

    /// <summary>Whether a form on show, or one left with them, holds anything a Save would write.</summary>
    public bool HasUnsavedChanges => Forms().Any(form => form.HasUnsavedChanges);

    /// <summary>
    /// Why the window mustn't close without asking, in words for the user, found at once and asking nothing: what's
    /// running that closing would cut short, or that a form holds unsaved changes; null when it may close. For Windows'
    /// question before it ends the app, which wants its answer there and then (<see cref="AppStatus.KeepsWindowOpen"/>).
    /// </summary>
    public string? KeepsWindowOpen => BusyWith() ?? (HasUnsavedChanges ? UnsavedChangesReason : null);

    /// <summary>Keeps <paramref name="form"/> as one on show until the returned subscription goes.</summary>
    public IDisposable Watch(IUnsavedChangesScreen form)
    {
        _shown[form] = _shown.GetValueOrDefault(form) + 1;
        _leftUnsaved.Remove(form);
        return new Release(this, form);
    }

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

        foreach (var form in Forms().Where(form => form.HasUnsavedChanges).ToList())
        {
            if (!await ResolveAsync(form))
            {
                return false;
            }
        }

        _leftUnsaved.RemoveWhere(form => !form.HasUnsavedChanges);
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

    /// <summary>Why something running keeps the window open (<see cref="IBusyWork"/>); null while nothing does.</summary>
    private string? BusyWith() =>
        _services.GetServices<IBusyWork>().Select(work => work.KeepsWindowOpen).FirstOrDefault(why => why is not null);

    private IEnumerable<IUnsavedChangesScreen> Forms() =>
        _shown.Keys
            .Concat(_leftUnsaved)
            .Concat(_services.GetServices<IOpenForms>().SelectMany(view => view.OpenForms))
            .Distinct<IUnsavedChangesScreen>(ReferenceEqualityComparer.Instance);

    private sealed class Release(UnsavedChangesGuard guard, IUnsavedChangesScreen form) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            if (guard._shown.GetValueOrDefault(form) <= 1)
            {
                guard._shown.Remove(form);
                if (form.HasUnsavedChanges)
                {
                    guard._leftUnsaved.Add(form);
                }
            }
            else
            {
                guard._shown[form]--;
            }
        }
    }
}
