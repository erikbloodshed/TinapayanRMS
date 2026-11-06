using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AwesomeAssertions;
using Syncfusion.Licensing;
using Syncfusion.Windows.Shared;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// A ChromelessWindow with UseNativeChrome, as MainWindow is, sent the WM_DPICHANGED that Windows sends when the
/// window moves to a monitor with another scale. WPF raises DpiChanged when it handles the message.
/// </summary>
public class ChromelessWindowDpiTests
{
    private const int WmDpiChanged = 0x02E0;

    [Test]
    public void ChromelessWindow_keeps_DPI_changes_from_WPF()
    {
        // Why ChromelessWindowDpi exists. If this starts failing, Syncfusion has stopped swallowing the message, and
        // ChromelessWindowDpi can go.
        WpfSeesDpiChange(letWpfHandleDpiChanges: false).Should().BeFalse();
    }

    [Test]
    public void LetWpfHandleDpiChanges_lets_WPF_rescale_the_window()
    {
        WpfSeesDpiChange(letWpfHandleDpiChanges: true).Should().BeTrue();
    }

    private static bool WpfSeesDpiChange(bool letWpfHandleDpiChanges)
    {
        var licenseKey = Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", EnvironmentVariableTarget.Process)
            ?? Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", EnvironmentVariableTarget.User);
        Skip.When(licenseKey is null, "SYNCFUSION_LICENSE_KEY isn't set, and an unlicensed Syncfusion control shows a dialog.");

        var dpiChanged = false;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            ChromelessWindow? window = null;
            try
            {
                // Windows only sends WM_DPICHANGED to a per-monitor-aware window, as the app's manifest makes it; the
                // test host isn't one, so this thread creates its windows that way.
                _ = SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
                SyncfusionLicenseProvider.RegisterLicense(licenseKey);

                window = new ChromelessWindow
                {
                    UseNativeChrome = true,
                    Width = 400,
                    Height = 300,
                    Left = -20000,
                    Top = -20000,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };
                window.Show();
                OffscreenGrid.Pump();

                if (letWpfHandleDpiChanges)
                {
                    ChromelessWindowDpi.LetWpfHandleDpiChanges(window);
                }

                // Only from here: showing the window can raise it, as WPF matches the window to the monitor it's on.
                window.DpiChanged += (_, _) => dpiChanged = true;
                SendDpiChange(new WindowInteropHelper(window).Handle);
                OffscreenGrid.Pump();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return dpiChanged;
    }

    /// <summary>A move to a monitor at another scale: 150% from 100%, or 100% from anything else, with the window's own rect as the suggested one.</summary>
    private static void SendDpiChange(IntPtr hwnd)
    {
        var newDpi = GetDpiForWindow(hwnd) == 96 ? 144 : 96;
        GetWindowRect(hwnd, out var rect).Should().BeTrue();

        var suggested = Marshal.AllocHGlobal(Marshal.SizeOf<Rect32>());
        try
        {
            Marshal.StructureToPtr(rect, suggested, false);
            _ = SendMessage(hwnd, WmDpiChanged, (IntPtr)((newDpi << 16) | newDpi), suggested);
        }
        finally
        {
            Marshal.FreeHGlobal(suggested);
        }
    }

    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = -4;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
