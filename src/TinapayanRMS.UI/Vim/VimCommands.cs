using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ReactiveUI;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.ViewModels;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.Vim;

/// <summary>
/// Runs a command from the command line on the screen the keyboard was on when <c>:</c> was typed, or on the screen
/// showing when that was nothing of a screen's own (the tab strip). Each runs the screen's own command, as its button
/// does, so it asks and checks and saves exactly as a click would.
/// </summary>
internal static class VimCommands
{
    /// <summary>Runs <paramref name="command"/>. Null when it ran, else what to tell the user instead.</summary>
    public static string? Run(VimCommand command, Window window, DependencyObject? from)
    {
        var host = window as IVimHost;
        switch (command.Kind)
        {
            case VimCommandKind.Screen:
                return host?.ShowScreen(command.Screen) == true ? null : "There are no screens to switch to here.";

            case VimCommandKind.Settings:
                if (host is null)
                {
                    return "Settings opens from the main window.";
                }

                // A dialog, so it opens once the key that ran the command has finished.
                window.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => host.OpenSettings());
                return null;

            case VimCommandKind.Revert:
                return Execute(ScreenOf<IRevertScreen>(from, host)?.RevertCommand, "Revert isn't on this screen.", "There's nothing to revert.");
        }

        var screen = ScreenOf<IRecordScreen>(from, host);
        return command.Kind switch
        {
            VimCommandKind.Save => Execute(screen?.SaveCommand, "There's nothing to save on this screen.", "There's nothing to save."),
            VimCommandKind.New => Execute(screen?.NewCommand, "There's nothing to start here.", "A new one can't be started now."),
            VimCommandKind.Delete => Execute(screen?.DeleteCommand, "There's nothing to delete on this screen.", "There's nothing to delete."),
            VimCommandKind.Open when screen is null => "There's no list to open from on this screen.",
            VimCommandKind.Open => screen.OpenByName(command.Text) ? null : $"Nothing on this screen is named “{command.Text}”.",
            _ => null,
        };
    }

    /// <summary>Runs <paramref name="command"/> as its button does, errors and all going where they always go.</summary>
    private static string? Execute(ReactiveCommand<RxVoid, RxVoid>? command, string missing, string unavailable)
    {
        if (command is not ICommand button)
        {
            return missing;
        }

        if (!button.CanExecute(null))
        {
            return unavailable;
        }

        button.Execute(null);
        return null;
    }

    private static T? ScreenOf<T>(DependencyObject? from, IVimHost? host)
        where T : class =>
        DataContextsAbove(from).OfType<T>().FirstOrDefault()
        ?? DataContextsAbove(host?.CurrentScreen).OfType<T>().FirstOrDefault();

    private static IEnumerable<object?> DataContextsAbove(DependencyObject? element) =>
        VisualTreeWalk.SelfAndAncestors(element).OfType<FrameworkElement>().Select(ancestor => ancestor.DataContext);
}
