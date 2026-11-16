using TinapayanRMS.Presentation.App;
using BackgroundWork = TinapayanRMS.Maui.Services.BackgroundWork;

namespace TinapayanRMS.Maui;

/// <summary>The app's one window, over the page that holds the web view.</summary>
public sealed class App(IServiceProvider services) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        // The window's title, which the taskbar and Alt+Tab show; the page's title bar shows its heading instead.
        var window = new Window(new MainPage()) { Title = new MainWindowState().Title };

        // The window closing ends the app: the hourly backups stop and the log is flushed (BackgroundWork.Stop), then
        // the process ends there and then. Left to end by itself, it stayed for another 17 to 22 seconds after the window
        // had gone, in native teardown once the app's own code was done (its ProcessExit never even ran), and a start in
        // that time found it running (SingleInstance) and exited with no window to bring forward.
        window.Destroying += (_, _) =>
        {
            BackgroundWork.Stop(services);
            Environment.Exit(0);
        };
        return window;
    }
}
