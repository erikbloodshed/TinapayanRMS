using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AwesomeAssertions;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>A CenterScreen window centred in its monitor's work area as it opens, and shrunk to fit it.</summary>
public class StartupCenteringTests
{
    private const int MonitorDefaultToNearest = 2;

    [Test]
    public void Centered_puts_a_window_that_fits_in_the_middle_of_the_work_area()
    {
        StartupCentering.Centered(new Int32Rect(5, 5, 400, 300), new Int32Rect(0, 0, 1920, 1020))
            .Should().Be(new Int32Rect(760, 360, 400, 300));
    }

    [Test]
    public void Centered_shrinks_a_window_larger_than_the_work_area_to_it()
    {
        // The main window, 1400×800 at 125%, on a 1366×768 monitor left of the primary, its taskbar 48px.
        StartupCentering.Centered(new Int32Rect(0, 0, 1750, 1000), new Int32Rect(-1366, 0, 1366, 720))
            .Should().Be(new Int32Rect(-1366, 0, 1366, 720));
    }

    [Test]
    public void Centered_shrinks_only_the_side_that_does_not_fit()
    {
        StartupCentering.Centered(new Int32Rect(0, 0, 2000, 500), new Int32Rect(0, 48, 1920, 1032))
            .Should().Be(new Int32Rect(0, 314, 1920, 500));
    }

    [Test]
    public void CenterOnMonitor_fits_a_CenterScreen_window_larger_than_its_monitor_into_the_work_area() => StaThread.Run(() =>
    {
        // Made but never shown: WPF places a window as it makes its handle, so this doesn't flash up on the screen.
        var window = new Window
        {
            Width = SystemParameters.VirtualScreenWidth * 2,
            Height = SystemParameters.VirtualScreenHeight * 2,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            StartupCentering.CenterOnMonitor(window);

            GetWindowRect(hwnd, out var rect).Should().BeTrue();
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref info).Should().BeTrue();
            rect.Should().Be(info.Work);
        }
        finally
        {
            window.Close();
        }
    });

    [Test]
    public void CenterOnMonitor_leaves_a_window_placed_by_hand_where_it_is() => StaThread.Run(() =>
    {
        var window = new Window
        {
            Width = 400,
            Height = 300,
            Left = -20000,
            Top = -20000,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            GetWindowRect(hwnd, out var before).Should().BeTrue();

            StartupCentering.CenterOnMonitor(window);

            GetWindowRect(hwnd, out var after).Should().BeTrue();
            after.Should().Be(before);
        }
        finally
        {
            window.Close();
        }
    });

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Rect32(int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect32 Monitor;
        public Rect32 Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
