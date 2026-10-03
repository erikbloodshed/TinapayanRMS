using System.Windows;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Vim;

/// <summary>
/// A window's part in the Vim keys: its tabs for <c>gt</c> and <c>gT</c>, and on the main window, Back and Forward, the
/// screens the command line names, and Settings. A window that isn't one (a prompt) just has none of these.
/// </summary>
public interface IVimHost
{
    /// <summary>Shows the next visible tab (+1) or the one before (-1), round from the last to the first.</summary>
    void StepTab(int step);

    /// <summary>Back a screen, as Alt+Left. False when there was nowhere to go.</summary>
    bool GoBack() => false;

    /// <summary>Forward a screen, as Alt+Right. False when there was nowhere to go.</summary>
    bool GoForward() => false;

    /// <summary>Shows <paramref name="screen"/>'s tab, with the keyboard on its header. False when the window has none.</summary>
    bool ShowScreen(AppScreen screen) => false;

    /// <summary>Opens Settings. False when the window can't.</summary>
    bool OpenSettings() => false;

    /// <summary>The screen on show, for a command given with the keyboard on no screen of its own (the tab strip).</summary>
    FrameworkElement? CurrentScreen => null;
}
