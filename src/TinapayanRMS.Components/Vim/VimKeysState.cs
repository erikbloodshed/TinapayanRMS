using System.Windows.Input;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Presentation.ViewModels;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Components.Vim;

/// <summary>What running a command line came to: somewhere to go, the cheat sheet to show, or something to say.</summary>
public sealed record VimCommandResult(string? Message = null, string? Url = null, bool Help = false);

/// <summary>
/// The Vim keys for one page the window loads: whether they're on (the Windows user's choice, kept with the view state
/// and changed in Settings → Keyboard), and the screen on show, which the command line's <c>:w</c>, <c>:new</c>, <c>:delete</c>,
/// <c>:e name</c> and <c>:revert</c> run on: each screen says it's
/// showing (ScreenBase) and that it's gone.
/// </summary>
public sealed class VimKeysState(IViewStateStore viewState, ProductsViewModel products)
{
    private bool? _isOn;

    /// <summary>Raised when the keys are turned on or off in Settings.</summary>
    public event Action? Changed;

    public bool IsOn => _isOn ??= KeyboardSettingsViewModel.IsVimKeysSaved(viewState);

    /// <summary>The ViewModel of the screen on show, or null between screens.</summary>
    public object? Screen { get; private set; }

    public void Set(bool isOn)
    {
        _isOn = isOn;
        Changed?.Invoke();
    }

    public void Show(object screen) => Screen = screen;

    public void Hide(object screen)
    {
        if (ReferenceEquals(Screen, screen))
        {
            Screen = null;
        }
    }

    /// <summary>
    /// Runs what's typed after <c>:</c>. Each command runs the screen's own command, as its button does, so it asks and
    /// checks and saves exactly as a click would; a screen goes into the address for the browser to open.
    /// </summary>
    public VimCommandResult Run(string line)
    {
        if (VimCommandLine.Parse(line, out var error) is not { } command)
        {
            return new VimCommandResult(error);
        }

        var screen = Screen as IRecordScreen;
        return command.Kind switch
        {
            VimCommandKind.Screen when command.Screen == AppScreen.Print => Print(),
            VimCommandKind.Screen => new VimCommandResult(Url: UrlOf(command.Screen)),
            VimCommandKind.Settings => new VimCommandResult(Url: UrlOf(AppScreen.Settings)),
            VimCommandKind.Help => new VimCommandResult(Help: true),
            VimCommandKind.Revert => Execute((Screen as IRevertScreen)?.RevertCommand, "Revert isn't on this screen.", "There's nothing to revert."),
            VimCommandKind.Save => Execute(screen?.SaveCommand, "There's nothing to save on this screen.", "There's nothing to save."),
            VimCommandKind.New => Execute(screen?.NewCommand, "There's nothing to start here.", "A new one can't be started now."),
            VimCommandKind.Delete => Execute(screen?.DeleteCommand, "There's nothing to delete on this screen.", "There's nothing to delete."),
            VimCommandKind.Open when screen is null => new VimCommandResult("There's no list to open from on this screen."),
            VimCommandKind.Open => screen.OpenByName(command.Text) ? new VimCommandResult() : new VimCommandResult($"Nothing on this screen is named “{command.Text}”."),
            _ => new VimCommandResult(),
        };
    }

    /// <summary>The address of <paramref name="screen"/>, as the menu's links have it.</summary>
    public static string UrlOf(AppScreen screen) => screen switch
    {
        AppScreen.RawMaterials => "raw-materials",
        AppScreen.SubAssemblies => "sub-assemblies",
        AppScreen.Products => "products",
        AppScreen.Reports => "reports",
        AppScreen.PriceSimulator => "price-simulator",
        AppScreen.Settings => "settings",
        _ => "print",
    };

    // Print what Products has open, as its Print button does (which opens the Print screen with that choice); with
    // nothing open there, just the Print screen.
    private VimCommandResult Print()
    {
        if (products.PrintCommand.CanExecute())
        {
            ((ICommand)products.PrintCommand).Execute(null);
            return new VimCommandResult();
        }

        return new VimCommandResult(Url: UrlOf(AppScreen.Print));
    }

    /// <summary>Runs <paramref name="command"/> as its button does, errors and all going where they always go.</summary>
    private static VimCommandResult Execute(ICommand? command, string missing, string unavailable)
    {
        if (command is null)
        {
            return new VimCommandResult(missing);
        }

        if (!command.CanExecute(null))
        {
            return new VimCommandResult(unavailable);
        }

        command.Execute(null);
        return new VimCommandResult();
    }
}
