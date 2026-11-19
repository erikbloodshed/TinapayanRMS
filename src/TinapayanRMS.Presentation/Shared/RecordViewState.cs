using System.Reactive.Disposables;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.Shared;

/// <summary>
/// A record view (Sub-Assemblies): its list beside a strip of tabs, each holding a form of its own (<see cref="Tabs"/>), as
/// Visual Studio Code's editors are, so each record keeps its edits and its undo while another shows, and a record shows in
/// one tab at most. A record picked in the list opens in the preview tab, which the next pick replaces while it holds
/// nothing unsaved; one with changes, or kept by a double-click, stays. The view's own work (its load, the list's
/// actions) runs as the form showing does (<see cref="Runner"/>), so what it says shows on that form; and a form's
/// question asked while no page shows it goes to the view's own page (<see cref="AddTab"/>).
/// </summary>
/// <typeparam name="TForm">The view's form: one record's, a new one's, or a copy's.</typeparam>
public abstract class RecordViewState<TForm> : ScreenState, IOpenForms, IDisposable
    where TForm : ScreenState, ITabForm, IDisposable
{
    /// <summary>What ties each tab's form to the view, let go of as the tab closes.</summary>
    private readonly Dictionary<RecordTab<TForm>, IDisposable> _wiring = [];

    protected RecordViewState(ILogger logger)
        : base(logger)
    {
    }

    /// <summary>The view's tabs, and the one showing.</summary>
    public RecordTabs<TForm> Tabs { get; } = new();

    /// <summary>The form of the tab showing; null while no tab is open.</summary>
    public TForm? Form => Tabs.Active?.Form;

    /// <summary>
    /// The screen the view's own work runs as: the form showing, so what it says (a load that failed, a rename refused) shows
    /// where the user is looking, and its work counts with that form's; the view itself while no tab is open.
    /// </summary>
    protected ScreenState Runner => (ScreenState?)Form ?? this;

    /// <summary>A form for a new tab, tied to the view, holding nothing yet: a blank new record.</summary>
    protected abstract TForm NewForm();

    /// <summary>An address is waiting for the view's load to open its record: what the view's address says meanwhile stays.</summary>
    public abstract bool IsOpeningRecord { get; }

    /// <summary>What else ties <paramref name="form"/> to the view (its requests to other screens), let go of as its tab closes.</summary>
    protected virtual IDisposable Wire(TForm form) => Disposable.Empty;

    /// <summary>A tab with a new form, after the one showing, not shown yet.</summary>
    protected RecordTab<TForm> AddTab(bool isPreview)
    {
        var form = NewForm();
        var tab = Tabs.Add(form, isPreview);

        // Asked after the form's own page, which answers while it shows the form: a question the form asks while it isn't
        // showing (its tab closed with changes not saved) goes to the view's page, which shows the list beside it.
        _wiring[tab] = new CompositeDisposable(
            form.ConfirmDelete.RegisterHandler(async question => question.SetOutput(await ConfirmDelete.Handle(question.Input))),
            form.ConfirmUnsavedChanges.RegisterHandler(async question => question.SetOutput(await ConfirmUnsavedChanges.Handle(question.Input))),
            Wire(form));
        return tab;
    }

    /// <summary>
    /// The tab to open a record in that has no tab of its own: the preview tab while it holds nothing unsaved, else (keeping
    /// that one, now it holds changes) a new preview tab.
    /// </summary>
    protected RecordTab<TForm> PreviewSlot()
    {
        if (Tabs.Preview is { } preview)
        {
            if (!preview.Form.HasUnsavedChanges)
            {
                return preview;
            }

            preview.IsPreview = false;
        }

        return AddTab(isPreview: true);
    }

    /// <summary>
    /// Shows the saved record <paramref name="recordId"/>: its own tab, if it has one (kept, with <paramref name="keep"/>), or
    /// else the preview slot (<see cref="PreviewSlot"/>), or a kept tab of its own with <paramref name="keep"/>, which
    /// <paramref name="load"/> fills. Returns the form that shows it.
    /// </summary>
    protected TForm OpenInTab(int recordId, bool keep, Action<TForm> load)
    {
        if (Tabs.Holding(recordId) is { } open)
        {
            if (keep)
            {
                open.IsPreview = false;
            }

            Tabs.Activate(open);
            return open.Form;
        }

        var tab = keep ? AddTab(isPreview: false) : PreviewSlot();
        load(tab.Form);
        Tabs.Activate(tab);
        return tab.Form;
    }

    /// <summary>Shows <paramref name="tab"/>: a click on it, its address (Back, Forward), or another tab closed.</summary>
    public void Show(RecordTab<TForm> tab) => Tabs.Activate(tab);

    /// <summary>Keeps <paramref name="tab"/> open: a double-click on it, or on its record in the list. It's no longer the preview tab.</summary>
    public void Keep(RecordTab<TForm> tab) => tab.IsPreview = false;

    /// <summary>Shows the tab numbered <paramref name="number"/>, the one a new record's address names (sub-assemblies/new/3), if it's still open.</summary>
    public void ShowNumbered(int number)
    {
        if (Tabs.Numbered(number) is { } tab)
        {
            Tabs.Activate(tab);
        }
    }

    /// <summary>
    /// Closes <paramref name="tab"/> (its ✕), once its form's unsaved changes are saved or thrown away: it's shown first, so
    /// the question is asked of the form in view. False when Cancel keeps it, or a Save refused does.
    /// </summary>
    public async Task<bool> CloseTabAsync(RecordTab<TForm> tab)
    {
        if (!Tabs.All.Contains(tab))
        {
            return true;
        }

        if (tab.Form.HasUnsavedChanges)
        {
            Tabs.Activate(tab);
            if (!await tab.Form.ResolveUnsavedChangesAsync(revert: false))
            {
                return false;
            }
        }

        Drop(tab);
        return true;
    }

    /// <summary>Takes <paramref name="tab"/> away with its form, asking nothing: its record is gone, or its changes were dealt with.</summary>
    protected void Drop(RecordTab<TForm> tab)
    {
        Tabs.Remove(tab);
        if (_wiring.Remove(tab, out var wiring))
        {
            wiring.Dispose();
        }

        tab.Form.Dispose();
    }

    IReadOnlyList<IUnsavedChangesScreen> IOpenForms.OpenForms => [.. Tabs.All.Select(tab => tab.Form)];

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        foreach (var (tab, wiring) in _wiring)
        {
            wiring.Dispose();
            tab.Form.Dispose();
        }

        _wiring.Clear();
    }
}
