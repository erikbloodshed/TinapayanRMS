using TinapayanRMS.Presentation.ViewModels;
using BackgroundWork = TinapayanRMS.Maui.Services.BackgroundWork;

namespace TinapayanRMS.Maui;

/// <summary>The app's one window, over the page that holds the web view.</summary>
public sealed class App(IServiceProvider services) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        // The WPF window's title, which the taskbar and Alt+Tab show; the page's title bar shows its heading instead.
        var window = new Window(new MainPage()) { Title = new MainWindowViewModel().Title };

        // The hourly backups stop with the window, which ends the app.
        window.Destroying += (_, _) => BackgroundWork.Stop(services);
        return window;
    }
}
