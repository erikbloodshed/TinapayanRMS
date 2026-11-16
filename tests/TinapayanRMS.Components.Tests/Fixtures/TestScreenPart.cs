// In the ViewModels' namespace, as a screen's parts are: ScreenBase takes a get-only property of a type from there for a
// part, and watches its properties, lists and commands too.
namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>A part of a test screen's ViewModel, as the Products screen's Packaging tab is: a command of its own.</summary>
public sealed class TestScreenPart : ObservableState
{
    public TestScreenPart()
    {
        RemoveCommand = ScreenCommand.Of(() => { }, () => _canRemove);
    }

    private bool _canRemove;

    /// <summary>Lets Remove run, saying so through the command alone, as a command whose test changed does.</summary>
    public void AllowRemove()
    {
        _canRemove = true;
        RemoveCommand.Refresh();
    }

    public ScreenCommand RemoveCommand { get; }
}
