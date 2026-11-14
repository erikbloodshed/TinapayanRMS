using System.IO;
using System.Reactive.Linq;
using ReactiveUI;
using TinapayanRMS.Infrastructure.Services;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// Shared behavior for every screen's ViewModel: a friendly ErrorMessage surfaced in the UI (as an
/// error toast, through ScreenBase's Notices) instead of letting a failed action or load fail
/// silently or crash the app, a SuccessMessage for what an action did (a toast too), and a
/// Delete-confirmation Interaction the screen answers with a dialog (ConfirmationHost). Introduced in Phase 13 once every screen needed the same things — not before, since
/// one screen needing them wouldn't have justified a shared base class over ReactiveObject directly.
/// </summary>
public abstract class ViewModelBase : ReactiveObject, IScreenPartHost
{
    private readonly ILogger _logger;

    protected ViewModelBase(ILogger logger)
    {
        _logger = logger.ForContext(GetType());
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        protected set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private string? _successMessage;
    /// <summary>What the last action did, e.g. "Cake Flour was saved.", shown as a success toast. Cleared with ErrorMessage before every action.</summary>
    public string? SuccessMessage
    {
        get => _successMessage;
        protected set => this.RaiseAndSetIfChanged(ref _successMessage, value);
    }

    /// <summary>
    /// True while a LoadCommand merges freshly loaded data into the screen (PLAN.md, tab state).
    /// Selection-driven subscriptions skip while it's set, so pointing a selection or picker at
    /// its refreshed copy doesn't reload the form or throw away unsaved edits.
    /// </summary>
    protected bool IsRefreshing { get; private set; }

    /// <summary>Runs <paramref name="merge"/>, the synchronous step of a LoadCommand, with IsRefreshing set.</summary>
    protected void ApplyRefresh(Action merge)
    {
        IsRefreshing = true;
        try
        {
            merge();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// True while a screen puts a whole form back, which is what an undo or a redo does. Subscriptions
    /// that would otherwise recompute from half-restored state, or apply the form a tab is being moved
    /// off, skip while it's set. It's kept apart from IsRefreshing because that one also means "this
    /// data came from a load, so merge it rather than replace it", which a restore doesn't.
    /// </summary>
    protected bool IsRestoring { get; private set; }

    /// <summary>Runs <paramref name="restore"/>, a whole form being put back, with IsRestoring set.</summary>
    protected void ApplyRestore(Action restore)
    {
        IsRestoring = true;
        try
        {
            restore();
        }
        finally
        {
            IsRestoring = false;
        }
    }

    /// <summary>
    /// The screen is being written to wholesale — a load merging in fresh data, or an undo putting a
    /// form back. Either way the change isn't the user's, so nothing should react to it as an edit.
    /// </summary>
    protected bool IsBusyApplying => IsRefreshing || IsRestoring;

    /// <summary>Input: a confirmation message to show the user. Output: true if they confirmed. The screen asks it in a dialog (ScreenBase).</summary>
    public Interaction<string, bool> ConfirmDelete { get; } = new();

    /// <summary>
    /// Input: the question about a form's unsaved changes, naming the record. Output: whether to save them, throw them away, or
    /// stay. The screen asks it in a dialog of Yes, No and Cancel (ScreenBase).
    /// </summary>
    public Interaction<string, UnsavedChangesAnswer> ConfirmUnsavedChanges { get; } = new();

    /// <summary>Set while the question about unsaved changes is up, so a second one arriving meanwhile is turned away.</summary>
    private bool _isAskingAboutUnsavedChanges;

    /// <summary>
    /// <see cref="IUnsavedChangesScreen.ResolveUnsavedChangesAsync"/> for a screen: when <paramref name="isDirty"/>, asks
    /// about the changes to <paramref name="name"/> and runs <paramref name="save"/> (true when it went through) or
    /// <paramref name="discard"/> (null when nothing needs putting back). True when the form may be left. Whatever fails on
    /// the way, such as reading the record back to put the form as it was saved, is shown here and the form isn't left:
    /// whoever's asking is on the way to another screen, or out of the app, and has no notice of its own to say so in
    /// (a move to another tab is awaited by nothing, so the click would just do nothing).
    /// </summary>
    protected async Task<bool> AskAboutUnsavedChangesAsync(bool isDirty, string name, Func<Task<bool>> save, Func<Task>? discard)
    {
        if (!isDirty)
        {
            return true;
        }

        if (_isAskingAboutUnsavedChanges)
        {
            return false;
        }

        try
        {
            UnsavedChangesAnswer answer;
            _isAskingAboutUnsavedChanges = true;
            try
            {
                answer = await ConfirmUnsavedChanges.Handle(
                    $"Save the changes to {name}?\n\nYes saves them. No throws them away. Cancel keeps them, and you stay on {name}.");
            }
            finally
            {
                _isAskingAboutUnsavedChanges = false;
            }

            switch (answer)
            {
                case UnsavedChangesAnswer.Save:
                    return await save();
                case UnsavedChangesAnswer.DontSave:
                    if (discard is not null)
                    {
                        await discard();
                    }

                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
            return false;
        }
    }

    /// <summary>
    /// Input: what's being exported, which names the file ("Product Cost Summary"). Output: where the workbook was saved, or
    /// null when the user cancelled. The screen registers the handler (Reports and the Price Simulator, through ExcelWorkbook
    /// and IFileDialogs), since the grids it writes, in their sort, filter and columns, are the screen's.
    /// </summary>
    public Interaction<string, string?> ExportToExcel { get; } = new();

    /// <summary>The error a failed load showed, which the next load that succeeds takes away again; null when it's gone.</summary>
    private string? _loadFailure;

    /// <summary>
    /// The work the screen starts that nothing awaits, counted while it runs: every load, action and selection-driven load
    /// below goes through it, and so does what the screen's parts read on their own (their constructors take it).
    /// </summary>
    protected BackgroundWork Work { get; } = new();

    /// <summary>
    /// Completes once the screen has settled: nothing it started is still running, a pick's load and a table's reads
    /// included, and their results are on the screen. For whoever drives the screen without watching it, as the tests do.
    /// </summary>
    public Task WhenIdleAsync() => Work.WhenIdleAsync();

    /// <summary>
    /// Runs action, clearing any previous ErrorMessage and SuccessMessage first (which also closes their toasts). Whatever
    /// it throws is shown as ErrorMessage, in the words <see cref="Failure"/> gives it, and logged: a rule's refusal
    /// (BusinessRuleException) as written, what the database refused or couldn't do put plainly, and anything else, a bug,
    /// as "Something went wrong", logged as an error with its stack. Nothing reaches ReactiveUI, which would take the app
    /// down with it, and nothing fails silently.
    /// </summary>
    protected Task RunSafelyAsync(Func<Task> action) => Work.RunAsync(async () =>
    {
        ErrorMessage = null;
        SuccessMessage = null;
        _loadFailure = null;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    });

    /// <summary>The same for an action that's done when it returns.</summary>
    protected Task RunSafelyAsync(Action action) => RunSafelyAsync(() =>
    {
        action();
        return Task.CompletedTask;
    });

    /// <summary>
    /// Runs a load, a LoadCommand's body: a load that fails shows why and is logged as <see cref="RunSafelyAsync"/>'s
    /// actions are, rather than reaching ReactiveUI, and the next load tries again (showing the tab again). Unlike an
    /// action it leaves the notices alone, since a screen loads after its own Save too and mustn't take that Save's message
    /// away; but a load that succeeds takes away the error a failed one left.
    /// </summary>
    protected Task LoadSafelyAsync(Func<Task> load) => Work.RunAsync(async () =>
    {
        try
        {
            await load();
            ClearLoadFailure();
        }
        catch (OperationCanceledException)
        {
            // A newer load took over.
        }
        catch (Exception ex)
        {
            ShowLoadFailure(ex);
        }
    });

    /// <summary>
    /// Loads what each of <paramref name="values"/> needs as it comes (a record picked in a list), a newer value cancelling a
    /// load still running (Switch). A load that fails shows why and is logged, and the next value loads as usual: a failure
    /// reaching Switch would end the subscription, and from then on no pick would load anything until the app restarted.
    /// A load
    /// cancelled by a newer value isn't a failure, whatever it threw on its way out (a driver can report a command cancelled
    /// mid-read as its own exception). Nothing picked loads nothing, so it leaves a failed load's error showing: the
    /// failure itself can clear the pick, as Sub-Assemblies does.
    /// </summary>
    protected void LoadLatest<T>(IObservable<T> values, Func<T, CancellationToken, Task> load) =>
        Observable.Switch(values.Select(value => Observable.FromAsync(cancellationToken => Work.RunAsync(async () =>
            {
                try
                {
                    await load(value, cancellationToken);
                    if (value is not null)
                    {
                        ClearLoadFailure();
                    }
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    // A newer value took over.
                }
                catch (Exception ex)
                {
                    ShowLoadFailure(ex);
                }
            }))))
            .Subscribe();

    /// <summary>
    /// Shows <paramref name="exception"/> as ErrorMessage, in <see cref="Failure"/>'s words after <paramref name="context"/>
    /// when there is one ("Couldn't read …: "), and logs it: a refusal as a warning, anything else as an error. For a
    /// failure an action catches itself, such as a read nothing awaits.
    /// </summary>
    protected void ShowFailure(Exception exception, string? context = null)
    {
        var failure = Failure.Of(exception);
        ErrorMessage = context is null ? failure.Text : $"{context}: {failure.Text}";
        if (failure.IsRejection)
        {
            _logger.Warning(exception, "Refused: {Reason}", failure.Text);
        }
        else
        {
            _logger.Error(exception, "Failed: {Reason}", failure.Text);
        }
    }

    private void ShowLoadFailure(Exception exception)
    {
        ShowFailure(exception);
        _loadFailure = ErrorMessage;
    }

    private void ClearLoadFailure()
    {
        if (_loadFailure is not null && ErrorMessage == _loadFailure)
        {
            ErrorMessage = null;
        }

        _loadFailure = null;
    }

    /// <summary>
    /// Exports through <see cref="ExportToExcel"/> and says where the workbook went. A file that couldn't be written (open
    /// in Excel, or a folder Windows won't let the app write to) shows as any action's failure does.
    /// </summary>
    protected Task ExportToExcelAsync(string title) => RunSafelyAsync(async () =>
    {
        var path = await ExportToExcel.Handle(title);
        if (path is not null)
        {
            SuccessMessage = $"Saved {Path.GetFileName(path)} in {Path.GetDirectoryName(path)}.";
        }
    });

    /// <summary>Asks the user to confirm via ConfirmDelete, then runs action (through RunSafelyAsync) only if they agree.</summary>
    protected async Task RunWithConfirmationAsync(string confirmationMessage, Func<Task> action)
    {
        var confirmed = await ConfirmDelete.Handle(confirmationMessage);
        if (!confirmed)
        {
            return;
        }

        await RunSafelyAsync(action);
    }

    // What the screen's parts ask of it (IScreenPartHost): its own runners and messages, as its own actions use them.
    BackgroundWork IScreenPartHost.Work => Work;

    Task IScreenPartHost.RunSafelyAsync(Func<Task> action) => RunSafelyAsync(action);

    void IScreenPartHost.LoadLatest<T>(IObservable<T> values, Func<T, CancellationToken, Task> load) => LoadLatest(values, load);

    void IScreenPartHost.ShowError(string? message) => ErrorMessage = message;

    void IScreenPartHost.ShowSuccess(string? message) => SuccessMessage = message;

    void IScreenPartHost.ShowFailure(Exception exception, string? context) => ShowFailure(exception, context);
}
