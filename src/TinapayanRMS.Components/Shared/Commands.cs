using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using TinapayanRMS.Presentation.ViewModels;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// Running a ViewModel's commands from a component. A command is a <see cref="ScreenCommandBase"/> on a screen moved off
/// ReactiveUI (Raw Materials) and a ReactiveCommand on the others, so each is run as its own kind until every screen has
/// moved, when the ReactiveCommand cases go.
/// </summary>
public static class Commands
{
    public static bool CanRun(ICommand command, object? parameter = null) => command.CanExecute(parameter);

    /// <summary>Runs <paramref name="command"/> if it can run now, as its button does. Its ViewModel catches what it throws.</summary>
    public static Task Run(ICommand command) => command.CanExecute(null) ? ExecuteAsync(command) : Task.CompletedTask;

    /// <summary>Runs <paramref name="command"/> with <paramref name="parameter"/> if it can run now. Its ViewModel catches what it throws.</summary>
    public static async Task Run<TParam>(ReactiveCommand<TParam, RxVoid> command, TParam parameter)
    {
        if (((ICommand)command).CanExecute(parameter))
        {
            await command.Execute(parameter);
        }
    }

    /// <inheritdoc cref="Run{TParam}(ReactiveCommand{TParam, RxVoid}, TParam)"/>
    public static Task Run<TParam>(ScreenCommand<TParam> command, TParam parameter) =>
        command.CanExecute(parameter) ? command.ExecuteAsync(parameter) : Task.CompletedTask;

    /// <summary>
    /// Runs <paramref name="command"/> whatever it says about running now, as a screen's load runs, and waits for it to end.
    /// </summary>
    public static async Task ExecuteAsync(ICommand command)
    {
        switch (command)
        {
            case ScreenCommand screenCommand:
                await screenCommand.ExecuteAsync();
                break;
            case ReactiveCommand<RxVoid, RxVoid> reactiveCommand:
                await reactiveCommand.Execute();
                break;
            default:
                command.Execute(null);
                break;
        }
    }
}
