namespace TinapayanRMS.UI.ViewModels;

/// <summary>One of the main window's screens, as moving between them sees it.</summary>
/// <param name="Screen">Which one it is.</param>
/// <param name="CurrentRecord">The record it has open now, which Back comes back to; null for none.</param>
/// <param name="OpenRecord">Reopens a record, for Back and Forward; null for a screen with no record to reopen.</param>
/// <param name="UnsavedChanges">
/// Its form, when it has one that can hold unsaved changes. Print has Products': it prints the product as Products shows it,
/// unsaved edits and all (the user's choice), so moving between the two asks nothing.
/// </param>
public sealed record NavigationScreen(AppScreen Screen, Func<int?> CurrentRecord, Action<int>? OpenRecord = null, IUnsavedChangesScreen? UnsavedChanges = null)
{
    /// <summary>The screen with the record it has open now.</summary>
    public ScreenVisit Visit() => new(Screen, CurrentRecord());
}

/// <summary>
/// Moving between the main window's screens, with no screen's unsaved changes left behind: a link from one screen to a record
/// on another, Back and Forward, a tab picked, and closing the app. It knows nothing of tabs: MainWindow says which screen is
/// showing and shows the one asked for, so all of this can be tested without a window, which it couldn't while it was
/// MainWindow's own (moved out 2026-11-04, at the user's request, from a code review).
///
/// Nothing awaits a move, since it starts from a click, so what fails in one goes to <c>showFailure</c>: left to a task nobody
/// awaits, it only ever reached a warning in the log, and the click just did nothing.
/// </summary>
/// <param name="history">Back and Forward's stops.</param>
/// <param name="screens">Every screen. The order is the one closing asks about them in, after the one showing.</param>
/// <param name="showing">The screen showing; null before there is one.</param>
/// <param name="show">Shows a screen.</param>
/// <param name="showFailure">Says what went wrong in a move.</param>
public sealed class ScreenNavigator(
    NavigationHistory history,
    IReadOnlyList<NavigationScreen> screens,
    Func<AppScreen?> showing,
    Action<AppScreen> show,
    Action<Exception> showFailure)
{
    // Set while Back or Forward shows a screen, so the move isn't recorded as a new step.
    private bool _isStepping;

    // The forms already answered for while closing. No leaves a form as it is for the app to close over, still unsaved,
    // and showing the next screen mustn't ask about it again as if it were being left (found 2026-11-04, in the real app:
    // No to one of two forms asked about the second with the first showing, then about the first again).
    private readonly HashSet<IUnsavedChangesScreen> _answeredForClosing = [];

    /// <summary>Whether any screen's form holds unsaved changes, which closing the app asks about.</summary>
    public bool HasUnsavedChanges => screens.Any(screen => screen.UnsavedChanges is { HasUnsavedChanges: true });

    /// <summary>The screen showing when the window opens: where the history starts.</summary>
    public void Start()
    {
        if (showing() is { } screen)
        {
            history.Start(ScreenOf(screen).Visit());
        }
    }

    /// <summary>
    /// Any move from <paramref name="left"/> to <paramref name="shown"/> is a step (the menu, the tab strip, a tab closed, a
    /// link), with the screen left going into the history with the record it has open as it's left. Only Back and Forward
    /// aren't: they keep their own place in it.
    /// </summary>
    public void RecordStep(AppScreen left, AppScreen shown)
    {
        if (!_isStepping)
        {
            history.Visit(ScreenOf(left).Visit(), shown);
        }
    }

    /// <summary>
    /// The form whose unsaved changes a move from <paramref name="from"/> to <paramref name="to"/> would leave: null when
    /// <paramref name="from"/>'s form holds none, <paramref name="to"/> shows the same form (Products and Print), or it's
    /// been answered for while closing.
    /// </summary>
    public IUnsavedChangesScreen? UnsavedChangesLeft(AppScreen from, AppScreen to) =>
        ScreenOf(from).UnsavedChanges is { HasUnsavedChanges: true } form
        && !ReferenceEquals(ScreenOf(to).UnsavedChanges, form)
        && !_answeredForClosing.Contains(form)
            ? form
            : null;

    /// <summary>
    /// A link from one screen to a record on another: the screen showing is left, its unsaved changes asked about first;
    /// <paramref name="open"/> opens the record, the other screen asking about its own; then that screen shows. The record is
    /// opened before the screen shows, so a list not loaded yet has it waiting for the load that showing the screen starts.
    /// </summary>
    public async Task OpenOnAsync(AppScreen target, Func<Task<bool>> open)
    {
        try
        {
            if (showing() is { } current
                && UnsavedChangesLeft(current, target) is { } form
                && !await form.ResolveUnsavedChangesAsync(revert: true))
            {
                return;
            }

            if (await open())
            {
                show(target);
            }
        }
        catch (Exception ex)
        {
            showFailure(ex);
        }
    }

    /// <summary>Goes back one screen. See <see cref="Step"/>.</summary>
    public bool GoBack() => Step(back: true);

    /// <summary>Goes forward one screen. See <see cref="Step"/>.</summary>
    public bool GoForward() => Step(back: false);

    /// <summary>
    /// Runs <paramref name="then"/>, a move away from <paramref name="form"/>, once its unsaved changes are saved or thrown
    /// away, with Don't Save putting it back as it was saved, so they aren't there to come back to.
    /// </summary>
    public async Task AfterAskingAsync(IUnsavedChangesScreen form, Action then)
    {
        try
        {
            if (await form.ResolveUnsavedChangesAsync(revert: true))
            {
                then();
            }
        }
        catch (Exception ex)
        {
            showFailure(ex);
        }
    }

    /// <summary>
    /// Asks about each form's unsaved changes, the one showing first, each screen shown as it's asked about, then runs
    /// <paramref name="close"/>; a Cancel stops there and keeps the app open. Don't Save leaves a form as it is, since the app
    /// is closing over it.
    /// </summary>
    public async Task CloseAfterAskingAsync(Action close)
    {
        try
        {
            var current = showing();
            foreach (var screen in screens.OrderBy(screen => screen.Screen == current ? 0 : 1))
            {
                if (screen.UnsavedChanges is not { HasUnsavedChanges: true } form || !_answeredForClosing.Add(form))
                {
                    continue;
                }

                show(screen.Screen);
                if (!await form.ResolveUnsavedChangesAsync(revert: false))
                {
                    return;
                }
            }

            close();
        }
        catch (Exception ex)
        {
            showFailure(ex);
        }
        finally
        {
            _answeredForClosing.Clear();
        }
    }

    /// <summary>
    /// Goes back or forward one screen, and reopens the record that screen had open, unless its form holds unsaved changes,
    /// which Back never throws away: the screen then shows as it is. The record is asked for before the screen shows, as the
    /// links between screens do. The screen left is asked about its own unsaved changes first, and the step only taken once
    /// they're saved or thrown away, so a Cancel leaves the history where it was. False when there was nowhere to go.
    /// </summary>
    private bool Step(bool back)
    {
        if (showing() is not { } current || (back ? history.BackTarget : history.ForwardTarget) is not { } next)
        {
            return false;
        }

        if (UnsavedChangesLeft(current, next.Screen) is { } form)
        {
            _ = AfterAskingAsync(form, () => Step(back));
            return true;
        }

        var leaving = ScreenOf(current).Visit();
        if ((back ? history.GoBack(leaving) : history.GoForward(leaving)) is not { } target)
        {
            return false;
        }

        var screen = ScreenOf(target.Screen);
        if (target.RecordId is { } recordId
            && screen.OpenRecord is { } open
            && screen.CurrentRecord() != recordId
            && screen.UnsavedChanges is not { HasUnsavedChanges: true })
        {
            open(recordId);
        }

        _isStepping = true;
        try
        {
            show(target.Screen);
        }
        finally
        {
            _isStepping = false;
        }

        return true;
    }

    private NavigationScreen ScreenOf(AppScreen screen) => screens.First(candidate => candidate.Screen == screen);
}
