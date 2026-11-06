using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Opens a <see cref="WindowStartupLocation.CenterScreen"/> window in the middle of its monitor's work area, and no larger
/// than it. WPF centres such a window on the monitor the mouse is on, at the size it has on the monitor it was made on, and
/// only then does the window take the scale of the monitor it has moved to, so it can end up off centre. Nor does WPF
/// shrink a window larger than the work area: the main window, 1400×800, centred on a 1366×768 monitor would have had its
/// title bar above the top of the screen, out of reach of the mouse.
/// </summary>
public static class StartupCentering
{
    private const int MonitorDefaultToNearest = 2;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;

    /// <summary>
    /// Centres <paramref name="window"/> in the work area of the monitor WPF has put it on, shrunk to fit if it's larger, when
    /// it starts CenterScreen at its normal size. Call it from OnSourceInitialized, which comes after WPF has placed the
    /// window and before it shows: the window is already on that monitor, at its scale, so the move doesn't change it.
    /// </summary>
    public static void CenterOnMonitor(Window window)
    {
        if (window.WindowStartupLocation != WindowStartupLocation.CenterScreen || window.WindowState != WindowState.Normal)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (hwnd == IntPtr.Zero
            || !GetWindowRect(hwnd, out var rect)
            || !GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref monitor))
        {
            return;
        }

        var placed = Centered(rect.ToInt32Rect(), monitor.Work.ToInt32Rect());
        _ = SetWindowPos(hwnd, IntPtr.Zero, placed.X, placed.Y, placed.Width, placed.Height, SwpNoZOrder | SwpNoActivate);
    }

    /// <summary><paramref name="window"/> centred in <paramref name="workArea"/>, and no wider or taller than it.</summary>
    public static Int32Rect Centered(Int32Rect window, Int32Rect workArea)
    {
        var width = Math.Min(window.Width, workArea.Width);
        var height = Math.Min(window.Height, workArea.Height);
        return new Int32Rect(workArea.X + (workArea.Width - width) / 2, workArea.Y + (workArea.Height - height) / 2, width, height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Int32Rect ToInt32Rect() => new(Left, Top, Right - Left, Bottom - Top);
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

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, int flags);
}
