using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// Running a ViewModel's commands (<see cref="ScreenCommandBase"/>) from a component, awaited, so the component knows
/// when it's done.
/// </summary>
public static class Commands
{
    /// <summary>Runs <paramref name="command"/> if it can run now, as its button does. Its ViewModel catches what it throws.</summary>
    public static Task Run(ScreenCommand command) => command.CanExecute() ? command.ExecuteAsync() : Task.CompletedTask;

    /// <summary>Runs <paramref name="command"/> with <paramref name="parameter"/> if it can run now. Its ViewModel catches what it throws.</summary>
    public static Task Run<TParam>(ScreenCommand<TParam> command, TParam parameter) =>
        command.CanExecute(parameter) ? command.ExecuteAsync(parameter) : Task.CompletedTask;
}
