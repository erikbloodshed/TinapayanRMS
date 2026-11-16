using System.Windows.Input;

namespace TinapayanRMS.Presentation.Shared;

/// <summary>
/// A screen's action for a button, a menu or a key to run: a body, and whether it can run now. Whether it can run is asked
/// when it's wanted (a button being drawn, a key pressed) rather than pushed as it changes: a screen is drawn again whenever its
/// ViewModel or a part of it changes (ScreenBase), which is what a can-run test reads. It can't run while it's running,
/// and says so as it starts and ends (<see cref="ICommand.CanExecuteChanged"/>).
/// <para>
/// <see cref="ScreenCommand.ExecuteAsync"/> runs the body whatever CanExecute says: a button, a menu or a key asks first
/// (Commands.Run in the screens). The body is the screen's to keep from throwing (ViewModelBase.RunSafelyAsync); one run
/// through <see cref="ICommand.Execute"/>, which nothing awaits, that throws anyway goes to <see cref="UnawaitedRunFailed"/>.
/// </para>
/// </summary>
public abstract class ScreenCommandBase : ICommand
{
    private int _running;

    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// What a run nothing awaited (one started through <see cref="ICommand.Execute"/>, as a menu's item or a key does)
    /// failed with, for the app to log and show (UnhandledFailures in the screens). Logged here when nothing listens.
    /// </summary>
    public static event Action<Exception>? UnawaitedRunFailed;

    /// <summary>Whether a run is under way, which it can't run again until it ends.</summary>
    public bool IsRunning => _running > 0;

    /// <summary>Says whether it can run may have changed, for whoever follows <see cref="ICommand.CanExecuteChanged"/>.</summary>
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Runs <paramref name="body"/>, counted as running from now until it ends, however it ends.</summary>
    protected async Task RunAsync(Func<Task> body)
    {
        if (_running++ == 0)
        {
            Refresh();
        }

        try
        {
            await body();
        }
        finally
        {
            if (--_running == 0)
            {
                Refresh();
            }
        }
    }

    protected abstract bool CanExecuteParameter(object? parameter);

    protected abstract Task ExecuteParameterAsync(object? parameter);

    bool ICommand.CanExecute(object? parameter) => CanExecuteParameter(parameter);

    void ICommand.Execute(object? parameter) =>
        ExecuteParameterAsync(parameter).ContinueWith(
            static run => ReportUnawaited(run.Exception!.InnerExceptions is [var only] ? only : run.Exception),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private static void ReportUnawaited(Exception exception)
    {
        if (UnawaitedRunFailed is { } failed)
        {
            failed(exception);
        }
        else
        {
            Serilog.Log.Error(exception, "A command run with nothing awaiting it failed");
        }
    }
}

/// <summary>A <see cref="ScreenCommandBase"/> that takes nothing.</summary>
public sealed class ScreenCommand : ScreenCommandBase
{
    private readonly Func<Task> _body;
    private readonly Func<bool>? _canRun;

    /// <param name="body">What it does.</param>
    /// <param name="canRun">Whether it can run now, besides not running already; always, when there's none.</param>
    public ScreenCommand(Func<Task> body, Func<bool>? canRun = null)
    {
        _body = body;
        _canRun = canRun;
    }

    /// <summary>One whose body is done when it returns.</summary>
    public static ScreenCommand Of(Action body, Func<bool>? canRun = null) => new(() =>
    {
        body();
        return Task.CompletedTask;
    }, canRun);

    /// <summary>One that takes a <typeparamref name="T"/>, whose body is done when it returns.</summary>
    public static ScreenCommand<T> Of<T>(Action<T> body, Func<T, bool>? canRun = null) => new(parameter =>
    {
        body(parameter);
        return Task.CompletedTask;
    }, canRun);

    /// <summary>Whether it can run now: not while it's running, and only when the screen says it can.</summary>
    public bool CanExecute() => !IsRunning && (_canRun?.Invoke() ?? true);

    /// <summary>Runs it, whatever <see cref="CanExecute"/> says.</summary>
    public Task ExecuteAsync() => RunAsync(_body);

    protected override bool CanExecuteParameter(object? parameter) => CanExecute();

    protected override Task ExecuteParameterAsync(object? parameter) => ExecuteAsync();
}

/// <summary>A <see cref="ScreenCommandBase"/> that takes a <typeparamref name="T"/>: a menu item's material, a tile's product.</summary>
public sealed class ScreenCommand<T> : ScreenCommandBase
{
    private readonly Func<T, Task> _body;
    private readonly Func<T, bool>? _canRun;

    /// <param name="body">What it does with what it's given.</param>
    /// <param name="canRun">Whether it can run now with that, besides not running already; always, when there's none.</param>
    public ScreenCommand(Func<T, Task> body, Func<T, bool>? canRun = null)
    {
        _body = body;
        _canRun = canRun;
    }

    public bool CanExecute(T parameter) => !IsRunning && (_canRun?.Invoke(parameter) ?? true);

    /// <summary>Runs it with <paramref name="parameter"/>, whatever <see cref="CanExecute"/> says.</summary>
    public Task ExecuteAsync(T parameter) => RunAsync(() => _body(parameter));

    protected override bool CanExecuteParameter(object? parameter) => CanExecute(Cast(parameter));

    protected override Task ExecuteParameterAsync(object? parameter) => ExecuteAsync(Cast(parameter));

    // What ICommand hands over: nothing is T's default, anything else must be a T.
    private static T Cast(object? parameter) => parameter switch
    {
        null => default!,
        T value => value,
        _ => throw new InvalidOperationException(
            $"The command takes a {typeof(T).FullName}, but was given a {parameter.GetType().FullName}."),
    };
}
