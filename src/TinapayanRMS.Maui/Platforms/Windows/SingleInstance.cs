using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TinapayanRMS.Components;

namespace TinapayanRMS.Maui.WinUI;

/// <summary>
/// One copy of the app on the PC. Starting it again in the same Windows sign-in (the Desktop icon twice) brings the window
/// already open to the front, rather than a second copy that would run its own hourly backups and write the same view-state
/// files. Each copy on the disk is its own app within a sign-in, so one run from source doesn't stop the installed copy,
/// nor the other way round. Across sign-ins it's one copy for the whole PC (OneCopyPerPc): a start refused that way says
/// why and ends.
/// </summary>
public static partial class SingleInstance
{
    private const int ShowRestore = 9;

    // Held for the life of the process: Windows releases it as the process ends.
    private static Mutex? _claim;

    /// <summary>
    /// Whether this copy may run: false when the window of the one already running in this sign-in was brought forward,
    /// or when another sign-in has the app open, which a message box has said.
    /// </summary>
    public static bool Claim()
    {
        _claim = new Mutex(initiallyOwned: true, $@"Local\TinapayanRMS.Maui-{MauiProgram.CopyKey}", out var first);
        if (!first)
        {
            ShowFirst();
            return false;
        }

        using var me = Process.GetCurrentProcess();
        if (OneCopyPerPc.RefusalFor(RunningCopies(me.ProcessName), me.Id, me.SessionId) is { } refusal)
        {
            StartupMessage.ShowInformation(refusal);
            return false;
        }

        return true;
    }

    /// <summary>
    /// This app's processes, in every Windows session: the session comes from the system's list of processes, so no
    /// administrator is needed to read another user's. One that ends while it's read is left out.
    /// </summary>
    private static List<OneCopyPerPc.RunningCopy> RunningCopies(string appProcessName)
    {
        var running = new List<OneCopyPerPc.RunningCopy>();
        foreach (var process in Process.GetProcessesByName(appProcessName))
        {
            using (process)
            {
                try
                {
                    running.Add(new OneCopyPerPc.RunningCopy(process.Id, process.SessionId));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // It ended while it was being read.
                }
            }
        }

        return running;
    }

    private static void ShowFirst()
    {
        using var me = Process.GetCurrentProcess();
        var processes = Process.GetProcessesByName(me.ProcessName);
        try
        {
            var running = processes.FirstOrDefault(p => p.Id != me.Id && p.MainWindowHandle != 0 && p.MainModule?.FileName == me.MainModule?.FileName);
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
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
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
