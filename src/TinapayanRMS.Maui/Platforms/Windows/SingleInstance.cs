using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TinapayanRMS.Maui.WinUI;

/// <summary>
/// One copy of the app for the Windows user: starting it again (the Desktop icon twice) brings the window already open
/// to the front, rather than a second copy that would run its own hourly backups and write the same view-state files.
/// Each copy on the disk is its own app, so one run from source doesn't stop the installed copy, nor the other way round.
/// The WPF app keeps to one copy of its own the same way (SingleInstanceGuard): the two apps can be open at once.
/// </summary>
public static partial class SingleInstance
{
    private const int ShowRestore = 9;

    // Held for the life of the process: Windows releases it as the process ends.
    private static Mutex? _claim;

    /// <summary>Whether this is the only copy; false when the window of the one already running was brought forward.</summary>
    public static bool Claim()
    {
        var folder = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppContext.BaseDirectory.ToUpperInvariant())))[..16];
        _claim = new Mutex(initiallyOwned: true, $@"Local\TinapayanRMS.Maui-{folder}", out var first);
        if (!first)
        {
            ShowFirst();
        }

        return first;
    }

    private static void ShowFirst()
    {
        using var me = Process.GetCurrentProcess();
        var running = Process.GetProcessesByName(me.ProcessName)
            .FirstOrDefault(p => p.Id != me.Id && p.MainWindowHandle != 0 && p.MainModule?.FileName == me.MainModule?.FileName);
        if (running is null)
        {
            return;
        }

        if (IsIconic(running.MainWindowHandle))
        {
            ShowWindow(running.MainWindowHandle, ShowRestore);
        }

        SetForegroundWindow(running.MainWindowHandle);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);
}
