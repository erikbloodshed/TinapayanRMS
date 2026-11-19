using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Maui.Platform;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Serilog;
using TinapayanRMS.Components.Shared;
using Windows.Graphics;
using WinRT.Interop;

namespace TinapayanRMS.Maui.WinUI;

/// <summary>
/// The window behind the page's title bar (IAppWindow): its Minimize, Maximize and Close, and the bar's caption. The
/// window has no title bar of Windows' (WindowsWindow), so the page tells it where its bar is and where the bar's buttons
/// are, and Windows takes the gaps between the buttons as the caption through the window's non-client input
/// (InputNonClientPointerSource): dragging it moves the window and snaps it to an edge, a double-click maximizes or
/// restores it, and a right-click opens the system menu, done by Windows itself as on any title bar. The buttons stay
/// the page's, which draws and clicks them.
/// </summary>
public sealed partial class AppWindowChrome : IAppWindow
{
    private const int WmSysCommand = 0x0112;
    private const int ScClose = 0xF060;

    /// <summary>
    /// MAUI's own switch for its title bar's row, which it turns off for a window without a caption (WS_CAPTION). This one
    /// keeps its caption, as the Windows App SDK draws a window with no title bar, so MAUI is told here. Internal to MAUI:
    /// if a MAUI update renames it, the row stays and the log says why.
    /// </summary>
    private static readonly MethodInfo? SetMauiTitleBarVisibility = typeof(NavigationRootManager).GetMethod(
        "SetTitleBarVisibility", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(bool)]);

    /// <summary>Set once MAUI's switch is missing or has failed: it isn't tried again.</summary>
    private static bool _titleBarRowFailed;

    private Microsoft.UI.Xaml.Window? _window;
    private bool _wasMaximized;
    private (DeviceRect TitleBar, IReadOnlyList<DeviceRect> Buttons)? _titleBar;

    public event Action? StateChanged;

    public bool IsMaximized => Presenter is { State: OverlappedPresenterState.Maximized };

    private OverlappedPresenter? Presenter => _window?.AppWindow.Presenter as OverlappedPresenter;

    /// <summary>Takes the window once it's made (WindowsWindow.Configure); the page's title bar may ask before then, and is ignored.</summary>
    internal void Attach(Microsoft.UI.Xaml.Window window)
    {
        _window = window;
        _wasMaximized = IsMaximized;

        // Maximizing or restoring changes the window's size: the title bar's middle button follows it.
        window.AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange && IsMaximized != _wasMaximized)
            {
                _wasMaximized = IsMaximized;
                StateChanged?.Invoke();
            }
        };
    }

    /// <summary>
    /// After each change to the window's style (showing it, maximizing, restoring), once MAUI has dealt with it: MAUI
    /// shows its title bar's row again for a window with a caption, so it's hidden again, and the caption is set again
    /// over whatever MAUI's row put in its place.
    /// </summary>
    internal void AfterStyleChanged()
    {
        HideMauisTitleBarRow();
        ApplyCaption();
    }

    public void Minimize() => OnWindowThread(() => Presenter?.Minimize());

    public void ToggleMaximize() => OnWindowThread(() =>
    {
        if (IsMaximized)
        {
            Presenter?.Restore();
        }
        else
        {
            Presenter?.Maximize();
        }
    });

    /// <summary>
    /// Asks Windows to close the window, as its own Close button does (SC_CLOSE), so the window's Closing asks about a
    /// form's unsaved changes first (WindowsWindow).
    /// </summary>
    public void Close() => OnWindowThread(() =>
    {
        if (_window is not null)
        {
            _ = PostMessage(WindowNative.GetWindowHandle(_window), WmSysCommand, ScClose, 0);
        }
    });

    public void ClosePrintDialog() => OnWindowThread(() =>
    {
        if (MainPage.Current?.WebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 { CoreWebView2: { } webView })
        {
            _ = PrintDialog.CloseAsync(webView);
        }
    });

    public Task SavePageAsPdfAsync(string path) => MainThread.InvokeOnMainThreadAsync(() =>
        MainPage.Current?.WebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 { CoreWebView2: { } webView }
            ? PagePdf.SaveAsync(webView, path)
            : throw new InvalidOperationException("The window has no page to save as a PDF."));

    public void SetTitleBar(DeviceRect titleBar, IReadOnlyList<DeviceRect> buttons) => OnWindowThread(() =>
    {
        _titleBar = (titleBar, buttons);
        ApplyCaption();
    });

    private void ApplyCaption()
    {
        if (_window is null || _titleBar is not { } bar)
        {
            return;
        }

        var (left, top) = WebViewOffset();
        var caption = DeviceRect.Caption(bar.TitleBar, bar.Buttons)
            .Select(rect => new RectInt32(rect.X + left, rect.Y + top, rect.Width, rect.Height));
        InputNonClientPointerSource.GetForWindowId(_window.AppWindow.Id).SetRegionRects(NonClientRegionKind.Caption, [.. caption]);
    }

    private static void HideMauisTitleBarRow()
    {
        var manager = MainPage.Current?.Window?.Handler?.MauiContext?.Services.GetService(typeof(NavigationRootManager));
        if (manager is null || _titleBarRowFailed)
        {
            return;
        }

        if (SetMauiTitleBarVisibility is null)
        {
            Log.Warning("MAUI's NavigationRootManager.SetTitleBarVisibility(bool) wasn't found, so its title bar's row shows above the page's");
            _titleBarRowFailed = true;
            return;
        }

        // MAUI's internals, found by name: an update that keeps the name but changes what it does mustn't take the window
        // down. This runs in the window's message hook, on every show, maximize and restore, where an exception would end
        // the app; so a failure is logged once, and the row is left showing from then on.
        try
        {
            SetMauiTitleBarVisibility.Invoke(manager, [false]);
        }
        catch (Exception ex)
        {
            _titleBarRowFailed = true;
            Log.Warning(ex, "MAUI's NavigationRootManager.SetTitleBarVisibility(bool) failed, so its title bar's row shows above the page's");
        }
    }

    /// <summary>
    /// Where the web view's top left corner is in the window, in device pixels: the window's own (0, 0) while the web view
    /// fills it, as it does once MAUI's title bar's row is hidden.
    /// </summary>
    private static (int Left, int Top) WebViewOffset()
    {
        if (MainPage.Current?.WebView.Handler?.PlatformView is not FrameworkElement view || view.XamlRoot is null)
        {
            return (0, 0);
        }

        var origin = view.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        var scale = view.XamlRoot.RasterizationScale;
        return ((int)Math.Round(origin.X * scale), (int)Math.Round(origin.Y * scale));
    }

    // The page asks on the web view's dispatcher, which is the window's thread; anything else waits its turn there.
    private void OnWindowThread(Action action)
    {
        if (_window?.DispatcherQueue is not { } queue || queue.HasThreadAccess)
        {
            action();
        }
        else
        {
            queue.TryEnqueue(() => action());
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hwnd, int message, nint wParam, nint lParam);
}
