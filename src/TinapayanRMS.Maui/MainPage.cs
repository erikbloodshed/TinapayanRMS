using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
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
        WebView.BlazorWebViewInitialized += (_, e) =>
        {
            DisableZoom(e);
            RefuseDrops(e);
        };
        Content = WebView;
    }

    public BlazorWebView WebView { get; }

    /// <summary>The window's page, while it has one.</summary>
    public static MainPage? Current => Application.Current?.Windows is [var window, ..] ? window.Page as MainPage : null;

    /// <summary>
    /// The page is drawn at one size, the app's, as a desktop app's is: neither Ctrl with the mouse wheel, Ctrl with +, -
    /// or 0, nor a pinch on a touchpad or screen zooms it.
    /// </summary>
    private static void DisableZoom(BlazorWebViewInitializedEventArgs e)
    {
#if WINDOWS
        if (e.WebView.CoreWebView2?.Settings is { } settings)
        {
            settings.IsZoomControlEnabled = false;
            settings.IsPinchZoomEnabled = false;
        }
#endif
    }

    /// <summary>
    /// Nothing dragged in from outside the window lands in it: a file from File Explorer is refused, as the app has no use
    /// for one. MAUI 11.0.10 lets the web view take drops, and a file the page doesn't take is handed to Windows to open in
    /// its own program. The app's own drags (a table's lines, a list's rows onto a folder) follow the pointer, not the
    /// browser's drag and drop (app.js), so this doesn't touch them.
    /// </summary>
    private static void RefuseDrops(BlazorWebViewInitializedEventArgs e)
    {
#if WINDOWS
        e.WebView.AllowDrop = false;
#endif
    }
}
