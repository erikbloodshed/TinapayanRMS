using System.Runtime.InteropServices;
using Microsoft.Maui.Platform;
using Microsoft.UI.Windowing;
using Serilog;
using TinapayanRMS.Components.Shared;
using Windows.Graphics;
using WinRT.Interop;

namespace TinapayanRMS.Maui.WinUI;

/// <summary>
/// The app's window, set up chromeless, with the page's title bar in place of Windows' own
/// (<see cref="AppWindowChrome"/>), opening at 1400×800 in the middle of the monitor the mouse is on, and asking about a
/// form's unsaved changes before it closes (Save, Don't Save or Cancel, as moving away from the form asks), Windows'
/// own request to close it included (a sign-out, a shutdown, an install: <see cref="OnSessionEnding"/>).
/// </summary>
public static partial class WindowsWindow
{
    /// <summary>The window's size as it opens, in device-independent pixels.</summary>
    private const double OpeningWidth = 1400;
    private const double OpeningHeight = 800;

    public static void Configure(Microsoft.UI.Xaml.Window window)
    {
        var appWindow = window.AppWindow;
        var hwnd = WindowNative.GetWindowHandle(window);

        // No title bar of Windows': the page draws the title bar. The border
        // stays, so Windows still resizes the window from every edge and corner and draws its shadow. The Windows App SDK
        // does it by extending the content over a title bar it hides, Minimize, Maximize and Close included, and keeps the
        // window's caption (WS_CAPTION) for snapping and the taskbar; collapsed, that title bar is no height at all. MAUI
        // keeps a row of its own for a window with a caption, which AppWindowChrome hides once it's shown (OnMessage).
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        // Windows 11 rounds a window without a caption only when asked; Windows 10 has no such
        // attribute and keeps its square corners.
        var round = DwmCornerRound;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));

        OpenCentred(appWindow, hwnd);

        IPlatformApplication.Current?.Services.GetRequiredService<AppWindowChrome>().Attach(window);

        var mayClose = false;
        var deciding = false;
        appWindow.Closing += async (_, args) =>
        {
            if (mayClose)
            {
                return;
            }

            // Held open while the form is asked about, or a Save it's running finishes; closed again once it may be. Close
            // clicked again meanwhile waits on the first.
            args.Cancel = true;
            if (deciding)
            {
                return;
            }

            deciding = true;
            try
            {
                if (await FormMayBeLeftAsync())
                {
                    mayClose = true;
                    window.Close();
                }
            }
            finally
            {
                deciding = false;
            }
        };

        // Windows' own request to close the app, which MAUI's handling of the window's messages (OnMessage) can't answer.
        unsafe
        {
            if (!SetWindowSubclass(hwnd, &OnSessionEnding, SessionEndingSubclass, 0))
            {
                Log.Warning("Couldn't watch for Windows closing the app: a sign-out or an install would close it without asking");
            }
        }
    }

    /// <summary>
    /// Windows asking whether the app may end (WM_QUERYENDSESSION), then saying whether it does (WM_ENDSESSION). It asks
    /// before a sign-out or a shutdown, and for an install or uninstall that replaces the app's files (Windows
    /// Installer's Restart Manager: ENDSESSION_CLOSEAPP), and wants its answer at once. While a form has unsaved changes,
    /// or a backup or restore runs, the answer is no: the app stays open, and Windows shows why beside it in the list of
    /// apps a sign-out or a shutdown is waiting for (an installer's close of it fails at once). The window then asks
    /// what its Close asks, so the changes can be saved or let go, and the app closed. Otherwise the app ends as its
    /// window's closing ends it, once Windows says the session ends. An end that's forced (Sign out anyway, an installer
    /// told to force it) still ends it.
    /// </summary>
    [UnmanagedCallersOnly]
    private static unsafe nint OnSessionEnding(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            switch (message)
            {
                case WmQueryEndSession:
                    if (IPlatformApplication.Current?.Services.GetService<AppStatus>()?.KeepsWindowOpen is { } why)
                    {
                        Log.Information("Windows asked to close the app for {Ending}, and it stays open: {Why}", EndingFor(lParam), why);
                        _ = ShutdownBlockReasonCreate(hwnd, why);
                        AskAsCloseDoes(hwnd);
                        return 0;
                    }

                    _ = ShutdownBlockReasonDestroy(hwnd);
                    break;

                case WmEndSession:
                    _ = ShutdownBlockReasonDestroy(hwnd);
                    if (wParam != 0 && IPlatformApplication.Current?.Services is { } services)
                    {
                        Log.Information("Windows is ending the app, for {Ending}", EndingFor(lParam));
                        TinapayanRMS.Maui.App.End(services);
                    }

                    break;

                case WmNcDestroy:
                    _ = RemoveWindowSubclass(hwnd, &OnSessionEnding, id);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Never keep Windows from closing the app over an answer that couldn't be worked out.
            Log.Warning(ex, "Couldn't answer Windows asking to close the app");
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    /// <summary>
    /// The window's own question, as its Close asks it (the title bar's Close posts the same: AppWindowChrome), after
    /// Windows' question has had its answer. A minimized window comes back first, or nobody would see it.
    /// </summary>
    private static void AskAsCloseDoes(nint hwnd)
    {
        if (IsIconic(hwnd))
        {
            _ = PostMessage(hwnd, WmSysCommand, ScRestore, 0);
        }

        _ = PostMessage(hwnd, WmSysCommand, ScClose, 0);
    }

    /// <summary>What Windows ends the app for, from the flags it asks with, for the log.</summary>
    private static string EndingFor(nint flags) =>
        (flags & EndSessionCloseApp) != 0 ? "an install or update (Restart Manager)"
        : ((long)flags & EndSessionLogoff) != 0 ? "a sign-out"
        : "a shutdown or restart";

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
    /// In the middle of the work area of the monitor the mouse is on, and no larger than it. Moved onto that monitor
    /// first, so the window has taken its scale before it's sized for it: sized first, a move to a monitor of another
    /// scale would scale it again.
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

    // The question is the screen's own dialog, in the web view, so it's asked on the web view's dispatcher. A backup or
    // restore running in Settings → Database keeps the window open (UnsavedChangesGuard): it ends the process as it closes.
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
    private const uint WmQueryEndSession = 0x0011;
    private const uint WmEndSession = 0x0016;
    private const uint WmNcDestroy = 0x0082;
    private const nint EndSessionCloseApp = 0x1;
    private const long EndSessionLogoff = 0x80000000;
    private const int WmSysCommand = 0x0112;
    private const nint ScClose = 0xF060;
    private const nint ScRestore = 0xF120;
    private const nuint SessionEndingSubclass = 1;

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

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetWindowSubclass(
        nint hwnd, delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> subclass, nuint id, nuint data);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool RemoveWindowSubclass(
        nint hwnd, delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> subclass, nuint id);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShutdownBlockReasonCreate(nint hwnd, string reason);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShutdownBlockReasonDestroy(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hwnd, int message, nint wParam, nint lParam);
}
