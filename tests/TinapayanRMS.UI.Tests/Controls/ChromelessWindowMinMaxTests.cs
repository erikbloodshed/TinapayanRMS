using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AwesomeAssertions;
using Syncfusion.Windows.Shared;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// A ChromelessWindow with UseNativeChrome, as the app's windows are, off every monitor, which is where a minimized window
/// waits and where it still is as it starts coming back from the taskbar. Windows asks the window for its size limits
/// (WM_GETMINMAXINFO) then, and the answer limits the size it comes back at. The windows are made per-monitor aware, as the
/// app's manifest makes them, and never activated.
/// </summary>
public class ChromelessWindowMinMaxTests
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int ShowMinNoActive = 7;
    private const int ShowNoActivate = 4;
    private const int MonitorDefaultToNearest = 2;

    [Test]
    public void ChromelessWindow_brings_a_window_back_from_minimized_no_taller_than_the_monitor_nearest_it() => StaThread.RunLicensed(() =>
    {
        // Why ChromelessWindowMinMax exists. If this starts failing, ChromelessWindow no longer holds a window coming back to
        // the monitor nearest where minimized windows wait, and the helper can go.
        var (before, after) = MinimizedAndBack(answerWhileOffScreen: false);
        after.Height.Should().BeLessThan(before.Height);
    });

    [Test]
    public void AnswerWhileOffScreen_brings_a_window_back_from_minimized_at_its_size() => StaThread.RunLicensed(() =>
    {
        var (before, after) = MinimizedAndBack(answerWhileOffScreen: true);
        after.Should().Be(before);
    });

    [Test]
    public void AnswerWhileOffScreen_maximizes_a_window_on_no_monitor_onto_the_work_area_of_the_monitor_nearest_it() => StaThread.RunLicensed(() =>
    {
        _ = SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
        var window = OffScreenWindow(height: 300);
        try
        {
            var hwnd = Shown(window, answerWhileOffScreen: true);
            var answer = AskForMinMaxInfo(hwnd);
            var monitor = MonitorNearest(hwnd);

            answer.MaxPosition.Should().Be(new Point32(monitor.Work.Left - monitor.Monitor.Left, monitor.Work.Top - monitor.Monitor.Top));
            answer.MaxSize.Should().Be(new Point32(monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top));
            window.MaxHeight.Should().Be(double.PositiveInfinity, "ChromelessWindow didn't see the message, so didn't set the window's MaxHeight from it");
        }
        finally
        {
            window.Close();
        }
    });

    [Test]
    public void AnswerWhileOffScreen_leaves_a_window_on_a_monitor_to_ChromelessWindow() => StaThread.RunLicensed(() =>
    {
        // Made but never shown, so it doesn't flash up on the screen it's on.
        var workArea = SystemParameters.WorkArea;
        var window = OffScreenWindow(height: 300);
        window.Left = workArea.Left + 10;
        window.Top = workArea.Top + 10;
        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            ChromelessWindowMinMax.AnswerWhileOffScreen(window);

            _ = AskForMinMaxInfo(hwnd);

            window.MaxHeight.Should().NotBe(double.PositiveInfinity, "ChromelessWindow answered, and set the window's MaxHeight from the monitor");
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// A window as tall as Windows lets it be, minimized and brought back without being activated: its size before and after.
    /// Its taskbar button shows for the second this takes. Skipped where the monitor nearest it is as tall as the window,
    /// since then nothing could hold the window to it.
    /// </summary>
    private static (Size32 Before, Size32 After) MinimizedAndBack(bool answerWhileOffScreen)
    {
        _ = SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
        var window = OffScreenWindow(height: SystemParameters.VirtualScreenHeight);

        // As the main window has: one without a button is minimized onto the primary monitor's corner, not off every monitor.
        window.ShowInTaskbar = true;
        window.Title = nameof(ChromelessWindowMinMaxTests);
        try
        {
            var hwnd = Shown(window, answerWhileOffScreen);
            var before = SizeOf(hwnd);
            var nearest = MonitorNearest(hwnd).Work;
            Skip.When(nearest.Bottom - nearest.Top >= before.Height, "The monitor nearest the window is as tall as the window can be.");

            _ = ShowWindow(hwnd, ShowMinNoActive);
            OffscreenGrid.Pump();
            _ = ShowWindow(hwnd, ShowNoActivate);
            OffscreenGrid.Pump();

            return (before, SizeOf(hwnd));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Shows <paramref name="window"/>, with ChromelessWindowMinMax on it when asked, added as AppWindow adds it: once the
    /// window has its handle and before it shows, since ChromelessWindow answers the messages showing it brings.
    /// </summary>
    private static IntPtr Shown(ChromelessWindow window, bool answerWhileOffScreen)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (answerWhileOffScreen)
        {
            ChromelessWindowMinMax.AnswerWhileOffScreen(window);
        }

        window.Show();
        OffscreenGrid.Pump();
        return hwnd;
    }

    private static ChromelessWindow OffScreenWindow(double height) => new()
    {
        UseNativeChrome = true,
        Width = 400,
        Height = height,
        Left = -20000,
        Top = -20000,
        WindowStartupLocation = WindowStartupLocation.Manual,
        ShowActivated = false,
        ShowInTaskbar = false,
    };

    private static MinMaxInfo AskForMinMaxInfo(IntPtr hwnd)
    {
        var info = Marshal.AllocHGlobal(Marshal.SizeOf<MinMaxInfo>());
        try
        {
            // Windows' own starting values, for a window at the primary monitor's size with an 8px frame.
            Marshal.StructureToPtr(
                new MinMaxInfo { MaxSize = new Point32(1936, 1096), MaxPosition = new Point32(-8, -8), MinTrackSize = new Point32(16, 16), MaxTrackSize = new Point32(10000, 10000) },
                info,
                false);
            _ = SendMessage(hwnd, WmGetMinMaxInfo, IntPtr.Zero, info);
            return Marshal.PtrToStructure<MinMaxInfo>(info);
        }
        finally
        {
            Marshal.FreeHGlobal(info);
        }
    }

    private static Size32 SizeOf(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var rect).Should().BeTrue();
        return new Size32(rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static MonitorInfo MonitorNearest(IntPtr hwnd)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref info).Should().BeTrue();
        return info;
    }

    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = -4;

    private readonly record struct Size32(int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Point32(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point32 Reserved;
        public Point32 MaxSize;
        public Point32 MaxPosition;
        public Point32 MinTrackSize;
        public Point32 MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect32 Monitor;
        public Rect32 Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
