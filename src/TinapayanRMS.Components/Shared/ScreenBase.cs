using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Components.Vim;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// What every screen does with its state: shows its ErrorMessage and
/// SuccessMessage as toasts (<see cref="Notices"/>), answers its ConfirmDelete and ConfirmUnsavedChanges with a dialog
/// (<see cref="Confirmations"/>), runs its load each time the screen is opened, and draws
/// itself again whenever the screen state, one of its parts (the Products screen's Costing, say), one of their lists or one
/// of their commands changes. The screen state is injected, the page's own (they're scoped). The lists and commands are
/// watched as well as the properties, since a grid's rows or a button's enabled state change without a property of the
/// screen state changing. However many of them change together, the screen is drawn once for them all
/// (<see cref="RedrawGate"/>).
/// <para>
/// A move to another address asks nothing: every form that can hold unsaved changes is a record view's tab, which stays
/// as it is while another screen shows. Closing the window asks about each one with changes
/// (<see cref="UnsavedChangesGuard"/>), and reloading the page has the web view ask, its leave warning turned on here
/// while any of them holds changes.
/// </para>
/// </summary>
public abstract class ScreenBase<TState> : ComponentBase, IDisposable
    where TState : ScreenState
{
    private readonly List<IDisposable> _subscriptions = [];
    private readonly RedrawGate _redraws;
    private bool _redrawingNow;
    private bool? _leaveWarning;

    protected ScreenBase()
    {
        _redraws = new RedrawGate(InvokeAsync, StateHasChanged);
    }

    /// <summary>
    /// The screen's state: the page's own, as they're scoped (<see cref="ResolveState"/>), or the one it's given, a record
    /// view's form (FormBase).
    /// </summary>
    public TState? State { get; set; }

    [Inject] private IServiceProvider Services { get; set; } = null!;

    [Inject] private Notices Notices { get; set; } = null!;

    [Inject] private Confirmations Confirmations { get; set; } = null!;

    [Inject] private VimKeysState VimKeys { get; set; } = null!;

    [Inject] private NavigationManager Navigator { get; set; } = null!;

    [Inject] private IJSRuntime Browser { get; set; } = null!;

    [Inject] private UnsavedChangesGuard ClosingGuard { get; set; } = null!;

    [Inject] private Serilog.ILogger Logger { get; set; } = null!;

    /// <summary>The command that loads the screen, run each time it's opened; null for a screen with nothing to load.</summary>
    protected abstract ScreenCommand? LoadCommand { get; }

    /// <summary>The state the screen shows: the page's own, from its services.</summary>
    protected virtual TState ResolveState() => Services.GetRequiredService<TState>();

    protected override void OnInitialized()
    {
        base.OnInitialized();
        var state = State ??= ResolveState();

        _subscriptions.Add(state.ConfirmDelete.RegisterHandler(async interaction =>
            interaction.SetOutput(await Confirmations.AskAsync(interaction.Input))));
        _subscriptions.Add(state.ConfirmUnsavedChanges.RegisterHandler(async interaction =>
            interaction.SetOutput(await Confirmations.AskAboutUnsavedChangesAsync(interaction.Input))));

        // The screen on show is the one the Vim command line's :w, :new, :delete and :e run on.
        VimKeys.Show(state);
        _subscriptions.Add(new Unsubscriber(() => VimKeys.Hide(state)));

        state.PropertyChanged += OnStatePropertyChanged;
        _subscriptions.Add(new Unsubscriber(() => state.PropertyChanged -= OnStatePropertyChanged));
        WatchListsAndCommands(state);

        // Its parts: the screen states it holds for good (get-only), whose own changes the screen shows too.
        foreach (var property in WatchedOf(typeof(TState)).Parts)
        {
            if (property.GetValue(state) is INotifyPropertyChanged part and not ScreenState and not INotifyCollectionChanged)
            {
                part.PropertyChanged += OnPartPropertyChanged;
                _subscriptions.Add(new Unsubscriber(() => part.PropertyChanged -= OnPartPropertyChanged));
                WatchListsAndCommands(part);
            }
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (LoadCommand is { } load)
        {
            await load.ExecuteAsync();
        }
    }

    // The web view's own warning before the page reloads, for any form with unsaved changes, on show or not: a record
    // view's tabs not showing included.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        await SetLeaveWarningAsync(ClosingGuard.HasUnsavedChanges);
    }

    /// <summary>Keeps <paramref name="subscription"/> (an Interaction handler, say) until the screen goes.</summary>
    protected void AddSubscription(IDisposable subscription) => _subscriptions.Add(subscription);

    /// <summary>The page's toasts, for a failure the screen meets outside its state.</summary>
    protected Notices ScreenNotices => Notices;

    /// <summary>
    /// Moves the screen's address to <paramref name="path"/>, as it shows another tab or record. With
    /// <paramref name="replace"/>, it takes the place of the address in the history, so Back doesn't come back to it.
    /// </summary>
    protected void MoveAddressTo(string path, bool replace = false) => Navigator.NavigateTo(path, replace: replace);

    /// <summary>
    /// A redraw asked for while one is queued is left to it, whoever asks: Blazor asks after each event the screen
    /// handles too, as a click may have queued one.
    /// </summary>
    protected override bool ShouldRender() => _redrawingNow || !_redraws.IsPending;

    /// <summary>
    /// Draws the screen now, not on the dispatcher's next turn, for what has to follow the drawing: moving the cursor
    /// into a field the redraw fills in, or back to where an undo found it.
    /// </summary>
    protected Task RedrawNowAsync() => InvokeAsync(() =>
    {
        _redrawingNow = true;
        try
        {
            _redraws.Drawn();
            StateHasChanged();
        }
        finally
        {
            _redrawingNow = false;
        }
    });

    /// <summary>
    /// A property of the screen state changed. Called on whichever thread changed it, so anything that touches a component
    /// goes through InvokeAsync.
    /// </summary>
    protected virtual void OnStateChanged(string? propertyName)
    {
    }

    private void WatchListsAndCommands(object source)
    {
        var watched = WatchedOf(source.GetType());
        foreach (var property in watched.Lists)
        {
            if (property.GetValue(source) is INotifyCollectionChanged list)
            {
                list.CollectionChanged += OnListChanged;
                _subscriptions.Add(new Unsubscriber(() => list.CollectionChanged -= OnListChanged));
            }
        }

        foreach (var property in watched.Commands)
        {
            if (property.GetValue(source) is ICommand command)
            {
                command.CanExecuteChanged += OnCommandChanged;
                _subscriptions.Add(new Unsubscriber(() => command.CanExecuteChanged -= OnCommandChanged));
            }
        }
    }

    /// <summary>
    /// What a screen watches on a screen state or one of its parts, worked out once a type: the properties declared as a list
    /// (INotifyCollectionChanged) or a command (ICommand), and the parts, get-only properties of a type from the
    /// Presentation project's namespaces that tells of its changes and is neither a list (TableRows is watched as one) nor
    /// a screen's state.
    /// Chosen by the type a property is declared as, so a worked-out property (a caption, a filtered list) is never read for
    /// it: a list or command the screen should redraw for is declared as one.
    /// </summary>
    private static Watched WatchedOf(Type type) => WatchedTypes.GetOrAdd(type, static type =>
    {
        PropertyInfo[] properties = [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0)];
        static bool Is<T>(PropertyInfo property) => typeof(T).IsAssignableFrom(property.PropertyType);
        return new Watched(
            [.. properties.Where(Is<INotifyCollectionChanged>)],
            [.. properties.Where(p => Is<ICommand>(p) && !Is<INotifyCollectionChanged>(p))],
            [
                .. properties.Where(p => !p.CanWrite
                    && p.PropertyType.Namespace?.StartsWith(PresentationNamespaces, StringComparison.Ordinal) == true
                    && Is<INotifyPropertyChanged>(p)
                    && !Is<ScreenState>(p)
                    && !Is<INotifyCollectionChanged>(p)),
            ]);
    });

    // Every screen state, part and row is in one of these, the namespace of its folder (TinapayanRMS.Presentation.Products).
    private const string PresentationNamespaces = "TinapayanRMS.Presentation.";

    private static readonly ConcurrentDictionary<Type, Watched> WatchedTypes = new();

    private sealed record Watched(PropertyInfo[] Lists, PropertyInfo[] Commands, PropertyInfo[] Parts);

    private async Task SetLeaveWarningAsync(bool on)
    {
        if (_leaveWarning == on)
        {
            return;
        }

        _leaveWarning = on;
        try
        {
            await Browser.InvokeVoidAsync("tinapayan.setLeaveWarning", on);
        }
        catch (JSDisconnectedException)
        {
            // The page has gone, a reload taking it away: there's nothing left to warn.
        }
        catch (InvalidOperationException)
        {
            // A component rendered without a page to call (a test's, say).
        }
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ScreenState.ErrorMessage):
                Notices.ShowError(State!.ErrorMessage);
                break;
            case nameof(ScreenState.SuccessMessage):
                Notices.ShowSuccess(State!.SuccessMessage);
                break;
        }

        OnStateChanged(e.PropertyName);
        _redraws.Request();
    }

    private void OnPartPropertyChanged(object? sender, PropertyChangedEventArgs e) => _redraws.Request();

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => _redraws.Request();

    private void OnCommandChanged(object? sender, EventArgs e) => _redraws.Request();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            // What the screen said in red was about the screen. Left up, it would follow the user to the next one, which has
            // no message of its own to clear and so take it away.
            if (State?.ErrorMessage is not null)
            {
                Notices.ShowError(null);
            }

            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }
    }

    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
