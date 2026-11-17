namespace TinapayanRMS.Presentation.Shared;

/// <summary>
/// What a part of a screen's state (the Products screen's Packaging tab, say) asks of the screen it's part of: to run
/// its actions and reads as the screen's own, so what goes wrong shows on the screen and <see cref="ScreenState.WhenIdleAsync"/>
/// waits for them, and to show what they did. <see cref="ScreenState"/> implements it for every screen, explicitly, so
/// only a part holding the screen as its host reaches these. A part's own host interface adds what that part needs of
/// its screen alone (IProductPackagingHost), and a part whose commands are undo steps records them through the screen's
/// <see cref="IUndoRecorder"/> (<see cref="UndoRecorderExtensions.AsUndoStep"/>).
/// </summary>
public interface IScreenPartHost
{
    /// <summary>The screen's work nothing awaits, which a part's own reads are counted in (ScreenState.Work).</summary>
    BackgroundWork Work { get; }

    /// <summary>Runs an action of the part's as the screen runs its own: what it throws is shown and logged (ScreenState.RunSafelyAsync).</summary>
    Task RunSafelyAsync(Func<Task> action);

    /// <summary>Loads what each of <paramref name="values"/> needs, a newer value cancelling a load still running (ScreenState.LoadLatest).</summary>
    void LoadLatest<T>(IObservable<T> values, Func<T, CancellationToken, Task> load);

    /// <summary>Shows <paramref name="message"/> as the screen's error, or takes the one showing away with null.</summary>
    void ShowError(string? message);

    /// <summary>Shows <paramref name="message"/> as what the part did, or takes the one showing away with null.</summary>
    void ShowSuccess(string? message);

    /// <summary>Shows and logs a failure the part caught itself, after <paramref name="context"/> when there is one (ScreenState.ShowFailure).</summary>
    void ShowFailure(Exception exception, string? context = null);
}

/// <summary>
/// What a part of a record screen's form asks of the screen: to run as the screen's own (<see cref="IScreenPartHost"/>), to
/// record each change it makes as one step of the form's undo history (<see cref="IUndoRecorder"/>, the
/// <see cref="UndoRecorderExtensions.AsUndoStep"/> extension), and whether a change it hears of is the user's. Every record
/// screen is one (RecordScreenState); a part that needs more of its screen asks through an interface of its own on top of
/// this one.
/// </summary>
public interface IFormPartHost : IScreenPartHost, IUndoRecorder
{
    /// <summary>A load or an undo is writing the screen wholesale (ScreenState.IsBusyApplying): what changes meanwhile isn't the user's.</summary>
    bool IsBusyApplying { get; }

    /// <summary>Runs <paramref name="action"/> once <paramref name="question"/> is answered Yes, as the screen's own (ScreenState.RunWithConfirmationAsync).</summary>
    Task RunWithConfirmationAsync(string question, Func<Task> action);

    /// <summary>
    /// Runs <paramref name="merge"/>, fresh data a part read going into the screen, as a load's merge (ScreenState.ApplyRefresh):
    /// what it changes isn't the user's, so nothing takes it for an edit.
    /// </summary>
    void ApplyRefresh(Action merge);
}
