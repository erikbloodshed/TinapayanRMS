using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.Maui;
using TinapayanRMS.Components;

namespace TinapayanRMS.Maui;

/// <summary>
/// The web view the screens are drawn in, filling the window, its title bar included (the window has none of Windows'
/// own: WindowsWindow): wwwroot/index.html, with the app's Routes in it and HeadOutlet for the pages' titles.
/// </summary>
public sealed class MainPage : ContentPage
{
    public MainPage()
    {
        WebView = new BlazorWebView { HostPage = "wwwroot/index.html" };
        WebView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Routes) });
        WebView.RootComponents.Add(new RootComponent { Selector = "head::after", ComponentType = typeof(HeadOutlet) });
        Content = WebView;
    }

    public BlazorWebView WebView { get; }

    /// <summary>The window's page, while it has one.</summary>
    public static MainPage? Current => Application.Current?.Windows is [var window, ..] ? window.Page as MainPage : null;
}
