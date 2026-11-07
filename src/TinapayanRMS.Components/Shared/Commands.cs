using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Components.Shared;

/// <summary>Running a ViewModel's commands from a component, as a WPF Command binding did.</summary>
public static class Commands
{
    public static bool CanRun(ICommand command, object? parameter = null) => command.CanExecute(parameter);

    /// <summary>Runs <paramref name="command"/> with <paramref name="parameter"/> if it can run now. Its ViewModel catches what it throws.</summary>
    public static async Task Run<TParam>(ReactiveCommand<TParam, RxVoid> command, TParam parameter)
    {
        if (((ICommand)command).CanExecute(parameter))
        {
            await command.Execute(parameter);
        }
    }

    public static Task Run(ReactiveCommand<RxVoid, RxVoid> command) => Run(command, RxVoid.Default);
}
