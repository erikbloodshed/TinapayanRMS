using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Reflection;
using System.Windows.Input;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using ReactiveUI;
using ReactiveUI.Blazor;
using TinapayanRMS.Presentation.ViewModels;
using TinapayanRMS.Components.Vim;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// What every screen does with its ViewModel, as the WPF Views and their shared controls did: shows its ErrorMessage and
/// SuccessMessage as toasts (<see cref="Notices"/>), answers its ConfirmDelete and ConfirmUnsavedChanges with a dialog
/// (<see cref="Confirmations"/>), runs its load each time the screen is opened (the WPF app's tab activation), and draws
/// itself again whenever the ViewModel, one of its parts (the Products screen's Costing, say), one of their lists or one
/// of their commands changes. ReactiveInjectableComponentBase injects the ViewModel, the page's own (they're scoped),
/// and redraws on the ViewModel's property changes; the rest are added here, since a grid's rows or a button's enabled
/// state change without a property of the ViewModel changing. However many of them change together, the screen is drawn
/// once for them all (<see cref="RedrawGate"/>).
/// <para>
/// A screen whose form can hold unsaved changes (<see cref="IUnsavedChangesScreen"/>) is never left with them, as the
/// WPF ScreenNavigator saw to it: a move to another address (the tab strip, the menu, a link, Back and Forward) asks
/// about them first, and Cancel stays; closing the window asks the same (<see cref="UnsavedChangesGuard"/>), and
/// reloading its page has the web view ask.
/// </para>
/// </summary>
public abstract class ScreenBase<TViewModel> : ReactiveInjectableComponentBase<TViewModel>
    where TViewModel : ViewModelBase
{
    private readonly List<IDisposable> _subscriptions = [];
    private readonly RedrawGate _redraws;
    private bool _redrawingNow;
    private bool? _leaveWarning;

    /// <summary>The address the screen moves itself to as its form shows another record, which isn't a move away from it.</summary>
    private string? _ownMove;

    protected ScreenBase()
    {
        _redraws = new RedrawGate(InvokeAsync, StateHasChanged);
    }

    [Inject] private Notices Notices { get; set; } = null!;

    [Inject] private Confirmations Confirmations { get; set; } = null!;

    [Inject] private VimKeysState VimKeys { get; set; } = null!;

    [Inject] private NavigationManager Navigator { get; set; } = null!;

    [Inject] private IJSRuntime Browser { get; set; } = null!;

    [Inject] private UnsavedChangesGuard ClosingGuard { get; set; } = null!;

    [Inject] private Serilog.ILogger Logger { get; set; } = null!;

    /// <summary>The command that loads the screen, run each time it's opened; null for a screen with nothing to load.</summary>
    protected abstract ReactiveCommand<RxVoid, RxVoid>? LoadCommand { get; }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        var viewModel = ViewModel!;

        _subscriptions.Add(viewModel.ConfirmDelete.RegisterHandler(async interaction =>
            interaction.SetOutput(await Confirmations.AskAsync(interaction.Input))));
        _subscriptions.Add(viewModel.ConfirmUnsavedChanges.RegisterHandler(async interaction =>
            interaction.SetOutput(await Confirmations.AskAboutUnsavedChangesAsync(interaction.Input))));

        // The screen on show is the one the Vim command line's :w, :new, :delete and :e run on.
        VimKeys.Show(viewModel);
        _subscriptions.Add(new Unsubscriber(() => VimKeys.Hide(viewModel)));

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _subscriptions.Add(new Unsubscriber(() => viewModel.PropertyChanged -= OnViewModelPropertyChanged));
        WatchListsAndCommands(viewModel);

        // Its parts: the ViewModels it holds for good (get-only), whose own changes the screen shows too.
        foreach (var property in WatchedOf(typeof(TViewModel)).Parts)
        {
            if (property.GetValue(viewModel) is INotifyPropertyChanged part and not ViewModelBase and not INotifyCollectionChanged)
            {
                part.PropertyChanged += OnPartPropertyChanged;
                _subscriptions.Add(new Unsubscriber(() => part.PropertyChanged -= OnPartPropertyChanged));
                WatchListsAndCommands(part);
            }
        }

        if (UnsavedChangesForm is { } form)
        {
            _subscriptions.Add(ClosingGuard.Watch(form));
            _subscriptions.Add(Navigator.RegisterLocationChangingHandler(OnLocationChangingAsync));
            _subscriptions.Add(new Unsubscriber(() => SetLeaveWarningAsync(false).Forget(Logger)));
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (LoadCommand is { } load)
        {
            await load.Execute();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (UnsavedChangesForm is { } form)
        {
            await SetLeaveWarningAsync(form.HasUnsavedChanges);
        }
    }

    /// <summary>Keeps <paramref name="subscription"/> (an Interaction handler, say) until the screen goes.</summary>
    protected void AddSubscription(IDisposable subscription) => _subscriptions.Add(subscription);

    /// <summary>The page's toasts, for a failure the screen meets outside its ViewModel.</summary>
    protected Notices ScreenNotices => Notices;

    /// <summary>Whether <paramref name="command"/> can run now, for a button's Disabled.</summary>
    protected static bool CanRun(ICommand command) => command.CanExecute(null);

    /// <summary>
    /// Moves the screen's address to <paramref name="path"/>, as its form shows another record: no question about the
    /// form's changes, which the ViewModel has already asked about if there was one to ask.
    /// </summary>
    protected void MoveAddressTo(string path)
    {
        _ownMove = Navigator.ToAbsoluteUri(path).AbsoluteUri;
        Navigator.NavigateTo(path);
    }

    /// <summary>
    /// The form this screen keeps from being left with unsaved changes: its own ViewModel's, when it has one; Print's is
    /// Products', which it prints as Products shows it.
    /// </summary>
    protected virtual IUnsavedChangesScreen? UnsavedChangesForm => ViewModel as IUnsavedChangesScreen;

    /// <summary>
    /// Whether a move to <paramref name="path"/> (an address within the app, products/42) keeps the form showing, so its
    /// unsaved changes aren't asked about: Products' Print, which prints the product as Products shows it.
    /// </summary>
    protected virtual bool KeepsForm(string path) => false;

    /// <summary>Whether <paramref name="path"/> is this screen's own, a record's of it included (products/42).</summary>
    protected virtual bool IsOwnAddress(string path) => false;

    /// <summary>
    /// A redraw asked for while one is queued is left to it, whoever asks: ReactiveInjectableComponentBase asks on every
    /// property change too, after this class's own handler has queued one.
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
            StateHasChanged();
            _redraws.Drawn();
        }
        finally
        {
            _redrawingNow = false;
        }
    });

    /// <summary>
    /// A property of the ViewModel changed. Called on whichever thread changed it, so anything that touches a component
    /// goes through InvokeAsync.
    /// </summary>
    protected virtual void OnViewModelChanged(string? propertyName)
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
    /// What a screen watches on a ViewModel or one of its parts, worked out once a type: the properties declared as a list
    /// (INotifyCollectionChanged) or a command (ICommand), and the parts, get-only properties of a type from the ViewModels'
    /// namespace that tells of its changes and is neither a list (TableRows is watched as one) nor a screen's ViewModel.
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
                    && p.PropertyType.Namespace == typeof(ViewModelBase).Namespace
                    && Is<INotifyPropertyChanged>(p)
                    && !Is<ViewModelBase>(p)
                    && !Is<INotifyCollectionChanged>(p)),
            ]);
    });

    private static readonly ConcurrentDictionary<Type, Watched> WatchedTypes = new();

    private sealed record Watched(PropertyInfo[] Lists, PropertyInfo[] Commands, PropertyInfo[] Parts);

    // A move away while the form holds unsaved changes: asked about first. Another record of this screen's replaces the
    // form, so No leaves it for that one; another screen leaves it showing, so No puts it back as it was saved.
    // NavigateTo hands this the address as it was given, relative to the app (products/26) when the app gave it, and the
    // browser in full, so it's made whole before anything compares it.
    private async ValueTask OnLocationChangingAsync(LocationChangingContext context)
    {
        var own = _ownMove;
        _ownMove = null;
        var target = Navigator.ToAbsoluteUri(context.TargetLocation).AbsoluteUri;
        if (UnsavedChangesForm is not { HasUnsavedChanges: true } form || target == own)
        {
            return;
        }

        var path = Navigator.ToBaseRelativePath(target);
        if (KeepsForm(path) || target == Navigator.Uri)
        {
            return;
        }

        if (!await form.ResolveUnsavedChangesAsync(revert: !IsOwnAddress(path)))
        {
            context.PreventNavigation();
        }
    }

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModelBase.ErrorMessage):
                Notices.ShowError(ViewModel!.ErrorMessage);
                break;
            case nameof(ViewModelBase.SuccessMessage):
                Notices.ShowSuccess(ViewModel!.SuccessMessage);
                break;
        }

        OnViewModelChanged(e.PropertyName);
        _redraws.Request();
    }

    private void OnPartPropertyChanged(object? sender, PropertyChangedEventArgs e) => _redraws.Request();

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => _redraws.Request();

    private void OnCommandChanged(object? sender, EventArgs e) => _redraws.Request();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }

        base.Dispose(disposing);
    }

    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
