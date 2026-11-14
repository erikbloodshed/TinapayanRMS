namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// What a part of a screen's ViewModel (the Products screen's Packaging tab, say) asks of the screen it's part of: to run
/// its actions and reads as the screen's own, so what goes wrong shows on the screen and <see cref="ViewModelBase.WhenIdleAsync"/>
/// waits for them, and to show what they did. <see cref="ViewModelBase"/> implements it for every screen, explicitly, so
/// only a part holding the screen as its host reaches these. A part's own host interface adds what that part needs of
/// its screen alone (IProductPackagingHost), and a part whose commands are undo steps records them through the screen's
/// <see cref="IUndoRecorder"/> (<see cref="UndoRecorderExtensions.AsUndoStep"/>).
/// </summary>
public interface IScreenPartHost
{
    /// <summary>The screen's work nothing awaits, which a part's own reads are counted in (ViewModelBase.Work).</summary>
    BackgroundWork Work { get; }

    /// <summary>Runs an action of the part's as the screen runs its own: what it throws is shown and logged (ViewModelBase.RunSafelyAsync).</summary>
    Task RunSafelyAsync(Func<Task> action);

    /// <summary>Loads what each of <paramref name="values"/> needs, a newer value cancelling a load still running (ViewModelBase.LoadLatest).</summary>
    void LoadLatest<T>(IObservable<T> values, Func<T, CancellationToken, Task> load);

    /// <summary>Shows <paramref name="message"/> as the screen's error, or takes the one showing away with null.</summary>
    void ShowError(string? message);

    /// <summary>Shows <paramref name="message"/> as what the part did, or takes the one showing away with null.</summary>
    void ShowSuccess(string? message);

    /// <summary>Shows and logs a failure the part caught itself, after <paramref name="context"/> when there is one (ViewModelBase.ShowFailure).</summary>
    void ShowFailure(Exception exception, string? context = null);
}
