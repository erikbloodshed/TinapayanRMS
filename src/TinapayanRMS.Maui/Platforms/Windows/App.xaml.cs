namespace TinapayanRMS.Maui.WinUI;

/// <summary>The WinUI application Windows starts, which builds the MAUI app, unless it's open already.</summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        // Started again, it brings the window that's open forward instead.
        if (!SingleInstance.Claim())
        {
            Environment.Exit(0);
        }

        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
