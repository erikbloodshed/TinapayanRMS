using Serilog;

namespace TinapayanRMS.Maui.WinUI;

/// <summary>The WinUI application Windows starts, which builds the MAUI app, unless it's open already.</summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        // Started again, it brings the window that's open forward instead; or another sign-in has it open, which it has
        // said.
        if (!SingleInstance.Claim())
        {
            Environment.Exit(0);
        }

        InitializeComponent();

        // An exception on the window's own thread that nothing caught: WinUI ends the app, and MAUI logs nothing of it.
        // Logged, and left unhandled, since what was running is in a state nothing planned for.
        UnhandledException += (_, e) =>
        {
            Log.Fatal(e.Exception, "An exception nothing caught on the window's thread, and the app is ending: {Message}", e.Message);
            Log.CloseAndFlush();
        };
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
