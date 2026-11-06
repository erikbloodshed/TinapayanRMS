using System.Runtime.InteropServices;
using System.Windows.Interop;
using Syncfusion.Windows.Shared;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Keeps a ChromelessWindow's size as it comes back from the taskbar. ChromelessWindow (Syncfusion 35.1.43) answers
/// WM_GETMINMAXINFO, the window's maximized size and position and the largest it may be, from the monitor nearest the
/// window, and sets the window's MaxWidth and MaxHeight from it. As a window comes back from the taskbar, Windows asks
/// once the window is no longer minimized but before it has left the place minimized windows wait at, (-32000, -32000),
/// which is on no monitor. The monitor nearest that is the leftmost one, which needn't be the window's. With a 1366×768
/// monitor left of a 1920×1080 one, a window maximized on the larger came back 1366×720 in its corner, still maximized,
/// and a window at its normal size lost whatever it had over 720 pixels of height.
/// </summary>
public static class ChromelessWindowMinMax
{
    private const int WmSize = 0x0005;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int SizeMinimized = 1;
    private const int MonitorDefaultToNull = 0;
    private const int MonitorDefaultToNearest = 2;

    /// <summary>
    /// Answers WM_GETMINMAXINFO for <paramref name="window"/> while it's on no monitor, from the monitor it was on when it
    /// was minimized, which is the one it's coming back to: maximized, it fills that monitor's work area, as ChromelessWindow
    /// has it fill the work area of the monitor it's on. While the window is on a monitor, ChromelessWindow answers. Call
    /// it after ChromelessWindow's own OnSourceInitialized, which adds its hook, and before the window shows: HwndSource
    /// calls the newest hook first, and this one marks the message handled, so neither ChromelessWindow nor WPF sees it.
    /// WPF's MinWidth and MinHeight are left out of that one answer, which is harmless: the window comes back at the size
    /// it went down at, which kept them.
    /// </summary>
    public static void AnswerWhileOffScreen(ChromelessWindow window)
    {
        // By its handle: until the window first shows, it has no PresentationSource.
        HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(new OffScreenAnswer().Hook);
    }

    /// <summary>One window's hook, with where the window was when it was last minimized.</summary>
    private sealed class OffScreenAnswer
    {
        // The window's normal position as it was minimized, until it's back at its normal size or maximized. Windows only
        // keeps it while the window is minimized: by the time the window is coming back it's no longer minimized, and its
        // normal position reads as where it is, (-32000, -32000).
        private NativeRect? _minimizedFrom;

        public IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmSize)
            {
                _minimizedFrom = wParam == SizeMinimized && NormalPosition(hwnd) is { } normal ? normal : null;
            }
            else if (msg == WmGetMinMaxInfo
                && MonitorFromWindow(hwnd, MonitorDefaultToNull) == IntPtr.Zero
                && (_minimizedFrom ?? NormalPosition(hwnd)) is { } normal)
            {
                handled = Answer(normal, lParam);
            }

            return IntPtr.Zero;
        }
    }

    // In workspace coordinates, which are off screen coordinates by the taskbar's width when it's on the left or top of the
    // primary monitor: too little to change which monitor holds most of the window.
    private static NativeRect? NormalPosition(IntPtr hwnd)
    {
        var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        return GetWindowPlacement(hwnd, ref placement) ? placement.NormalPosition : null;
    }

    private static bool Answer(NativeRect normal, IntPtr lParam)
    {
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromRect(ref normal, MonitorDefaultToNearest), ref monitor))
        {
            return false;
        }

        var work = monitor.Work;
        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);

        // Relative to the monitor, as Windows takes them.
        info.MaxPosition = new NativePoint(work.Left - monitor.Monitor.Left, work.Top - monitor.Monitor.Top);
        info.MaxSize = new NativePoint(work.Width, work.Height);

        // Windows adjusts a maximized size and position given for the primary monitor to the monitor the window maximizes
        // on (MINMAXINFO's documentation), and the largest the window may be also bounds its maximized size. So that
        // holds it to the work area, but never below the window's normal size, which it may be coming back at.
        info.MaxTrackSize = new NativePoint(Math.Max(work.Width, normal.Width), Math.Max(work.Height, normal.Height));
        Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public NativePoint MinPosition;
        public NativePoint MaxPosition;
        public NativeRect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, int flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
}
