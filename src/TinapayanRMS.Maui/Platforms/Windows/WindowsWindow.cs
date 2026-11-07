using System.Runtime.InteropServices;
using Microsoft.Maui.Platform;
using Microsoft.UI.Windowing;
using Serilog;
using TinapayanRMS.Components.Shared;
using Windows.Graphics;
using WinRT.Interop;

namespace TinapayanRMS.Maui.WinUI;

/// <summary>
/// The app's window, set up as the WPF main window was: chromeless, with the page's title bar in place of Windows' own
/// (<see cref="AppWindowChrome"/>), opening at 1400×800 in the middle of the monitor the mouse is on, and asking about a
/// form's unsaved changes before it closes (Save, Don't Save or Cancel, as moving away from the form asks).
/// </summary>
public static partial class WindowsWindow
{
    /// <summary>The WPF main window's size, in device-independent pixels.</summary>
    private const double OpeningWidth = 1400;
    private const double OpeningHeight = 800;

    public static void Configure(Microsoft.UI.Xaml.Window window)
    {
        var appWindow = window.AppWindow;
        var hwnd = WindowNative.GetWindowHandle(window);

        // No title bar of Windows': the page draws the title bar, as the WPF app's ChromelessWindow drew it. The border
        // stays, so Windows still resizes the window from every edge and corner and draws its shadow. The Windows App SDK
        // does it by extending the content over a title bar it hides, Minimize, Maximize and Close included, and keeps the
        // window's caption (WS_CAPTION) for snapping and the taskbar; collapsed, that title bar is no height at all. MAUI
        // keeps a row of its own for a window with a caption, which AppWindowChrome hides once it's shown (OnMessage).
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        // Windows 11 rounds a window without a caption only when asked, as the WPF window found; Windows 10 has no such
        // attribute and keeps its square corners.
        var round = DwmCornerRound;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));

        OpenCentred(appWindow, hwnd);

        IPlatformApplication.Current?.Services.GetRequiredService<AppWindowChrome>().Attach(window);

        var mayClose = false;
        appWindow.Closing += async (_, args) =>
        {
            if (mayClose)
            {
                return;
            }

            // Held open while the form is asked about; closed again once it may be.
            args.Cancel = true;
            if (await FormMayBeLeftAsync())
            {
                mayClose = true;
                window.Close();
            }
        };
    }

    /// <summary>
    /// The window's messages, after MAUI's own handling of each: after a change to its style (showing it, maximizing or
    /// restoring it), MAUI has just shown its title bar's row again, which AppWindowChrome hides.
    /// </summary>
    public static void OnMessage(Microsoft.UI.Xaml.Window window, WindowsPlatformMessageEventArgs message)
    {
        if (message.MessageId == WmStyleChanged && message.WParam == GwlStyle)
        {
            IPlatformApplication.Current?.Services.GetRequiredService<AppWindowChrome>().AfterStyleChanged();
        }
    }

    /// <summary>
    /// In the middle of the work area of the monitor the mouse is on, as the WPF window opened (CenterScreen, with
    /// StartupCentering), and no larger than it. Moved onto that monitor first, so the window has taken its scale before
    /// it's sized for it: sized first, a move to a monitor of another scale would scale it again.
    /// </summary>
    private static void OpenCentred(AppWindow appWindow, nint hwnd)
    {
        var cursor = GetCursorPos(out var point) ? new PointInt32(point.X, point.Y) : new PointInt32(0, 0);
        var work = DisplayArea.GetFromPoint(cursor, DisplayAreaFallback.Primary).WorkArea;
        appWindow.Move(new PointInt32(work.X, work.Y));

        var scale = GetDpiForWindow(hwnd) / 96.0;
        var width = Math.Min((int)Math.Round(OpeningWidth * scale), work.Width);
        var height = Math.Min((int)Math.Round(OpeningHeight * scale), work.Height);
        appWindow.MoveAndResize(new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
    }

    // The question is the screen's own dialog, in the web view, so it's asked on the web view's dispatcher.
    private static async Task<bool> FormMayBeLeftAsync()
    {
        try
        {
            if (MainPage.Current is not { } page)
            {
                return true;
            }

            Task<bool>? resolving = null;
            var dispatched = await page.WebView.TryDispatchAsync(services =>
                resolving = services.GetRequiredService<UnsavedChangesGuard>().ResolveBeforeClosingAsync());
            return !dispatched || resolving is null || await resolving;
        }
        catch (Exception ex)
        {
            // Never keep the window from closing over a question that couldn't be asked.
            Log.Warning(ex, "Couldn't ask about unsaved changes as the window closed");
            return true;
        }
    }

    private const int WmStyleChanged = 0x007D;
    private const nint GwlStyle = -16;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point32 point);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
