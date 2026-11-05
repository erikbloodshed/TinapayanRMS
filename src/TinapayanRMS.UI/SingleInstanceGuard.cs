using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace TinapayanRMS.UI;

/// <summary>How asking the copy that's open went.</summary>
public enum SingleInstanceOutcome
{
    /// <summary>It closed while being asked, so this copy has the app and carries on.</summary>
    Claimed,

    /// <summary>It came to the front, so this copy has nothing to do but close.</summary>
    CameForward,

    /// <summary>It's there, but didn't answer in time.</summary>
    NotAnswering,

    /// <summary>It's there, but Windows won't let this copy near it: it runs as an administrator, or as another user.</summary>
    OutOfReach,
}

/// <summary>
/// Keeps the app to one copy for each Windows sign-in. Nothing in
/// the database notices a row changing after a screen read it, so two copies side by side saved over each other with
/// neither saying so. A copy that finds the app open asks that one to come to the front, and closes.
///
/// It goes by named kernel objects, so nothing is left behind to clear up: Windows frees the app when a copy crashes or is
/// ended in Task Manager, and the next one simply gets it. They're per sign-in (Local\), so another Windows user on this PC,
/// or another PC on the same database, isn't stopped: only a check made when saving could catch those.
///
/// A mutex belongs to the thread that took it, so <see cref="TryClaim"/> and <see cref="Dispose"/> go on one thread, the
/// UI thread in the app.
/// </summary>
/// <param name="name">What the copies know each other by: <see cref="AppName"/>, or a test's own.</param>
public sealed class SingleInstanceGuard(string name) : IDisposable
{
    public const string AppName = "TinapayanRMS";

    public const string NotAnsweringText =
        "TinapayanRMS is already open, but it isn't answering, so this copy is closing. "
        + "If its window doesn't come back, end it in Task Manager, then open the app again.";

    public const string OutOfReachText =
        "TinapayanRMS is already open, in a way this copy can't reach: as an administrator, or under another Windows user. "
        + "Use that window, or close it, then open the app again.";

    /// <summary>
    /// How long a second copy waits for the open one to answer. It's long because that copy may only just be starting: its
    /// window answers once it shows, and until then nothing has gone wrong.
    /// </summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often it asks again while it waits. Each answer wakes only one copy waiting for it, so copies started together
    /// each need to have asked for their own.
    /// </summary>
    private static readonly TimeSpan AskEvery = TimeSpan.FromMilliseconds(250);

    /// <summary>AllowSetForegroundWindow's ASFW_ANY.</summary>
    private const int AnyProcess = -1;

    /// <summary>ShowWindow's SW_RESTORE.</summary>
    private const int SwRestore = 9;

    private Mutex? _app;
    private EventWaitHandle? _comeForward;
    private EventWaitHandle? _cameForward;
    private RegisteredWaitHandle? _listening;
    private bool _hasApp;
    private bool _isOutOfReach;

    /// <summary>
    /// What a copy does as it starts, and whether it's the one that's open. If another has the app, that one's asked to come
    /// to the front instead, and this copy has nothing to do but close: it hasn't logged or started anything by then. Another
    /// build from another folder is said first, and so is one that can't be brought forward.
    /// </summary>
    /// <param name="patience">How long to wait for the open copy to answer: <see cref="Patience"/>.</param>
    /// <param name="openCopyElsewhere">Where the open copy runs from, when that isn't here: <see cref="OpenCopyElsewhere"/>.</param>
    /// <param name="say">Tells the user something.</param>
    public bool Claim(TimeSpan patience, Func<string?> openCopyElsewhere, Action<string, MessageBoxImage> say)
    {
        if (TryClaim())
        {
            return true;
        }

        if (openCopyElsewhere() is { } elsewhere)
        {
            say(ElsewhereText(elsewhere), MessageBoxImage.Information);
        }

        var outcome = AskOpenCopyToComeForward(patience);
        if (TextOf(outcome) is { } text)
        {
            say(text, MessageBoxImage.Warning);
        }

        return outcome == SingleInstanceOutcome.Claimed;
    }

    /// <summary>
    /// Takes the app if nothing else has it, and returns whether it did. A copy that crashed holding it counts as nothing:
    /// Windows hands its mutex on as abandoned, and there's nothing to take over but the name.
    /// </summary>
    public bool TryClaim()
    {
        try
        {
            _app = new Mutex(initiallyOwned: false, $@"Local\{name}");
            _comeForward = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.ComeForward");
            _cameForward = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.CameForward");
        }
        catch (UnauthorizedAccessException)
        {
            _isOutOfReach = true;
            return false;
        }

        try
        {
            _hasApp = _app.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            _hasApp = true;
        }

        return _hasApp;
    }

    /// <summary>
    /// After <see cref="TryClaim"/> found the app taken: asks that copy to come to the front, until it does, it closes, or
    /// <paramref name="patience"/> runs out. A request waits for a copy that isn't listening yet. If it closes meanwhile
    /// this copy gets the app, as it does when it's opened just as the other is on its way out.
    /// </summary>
    public SingleInstanceOutcome AskOpenCopyToComeForward(TimeSpan patience)
    {
        if (_isOutOfReach)
        {
            return SingleInstanceOutcome.OutOfReach;
        }

        // Windows lets whoever was just started take the foreground, but not a copy in the background, which without
        // this could only flash on the taskbar.
        _ = AllowSetForegroundWindow(AnyProcess);

        WaitHandle[] answers = [_app!, _cameForward!];
        var waited = Stopwatch.StartNew();
        try
        {
            do
            {
                _comeForward!.Set();
                switch (WaitHandle.WaitAny(answers, AskEvery))
                {
                    case 0:
                        _hasApp = true;
                        return SingleInstanceOutcome.Claimed;
                    case 1:
                        return SingleInstanceOutcome.CameForward;
                }
            }
            while (waited.Elapsed < patience);

            return SingleInstanceOutcome.NotAnswering;
        }
        catch (AbandonedMutexException)
        {
            _hasApp = true;
            return SingleInstanceOutcome.Claimed;
        }
    }

    /// <summary>
    /// From here on <paramref name="comeForward"/> is called, on a pool thread, each time another copy asks. It answers with
    /// <see cref="CameForward"/> once it has: an answer given before then couldn't tell a working copy from a frozen one.
    /// </summary>
    public void Listen(Action comeForward) =>
        _listening = ThreadPool.RegisterWaitForSingleObject(_comeForward!, (_, _) => comeForward(), null, Timeout.Infinite, executeOnlyOnce: false);

    /// <summary>Tells the copy that asked that this one is at the front.</summary>
    public void CameForward() => _cameForward!.Set();

    /// <summary>
    /// Puts <paramref name="window"/> in front, as it was before it was minimized, with the keyboard on whatever it's waiting
    /// for: a dialog or message box it owns, or else itself. WPF's own calls fall short with one of those open, as checked
    /// by running them: Activate leaves the keyboard on the window a message box has disabled, and Windows ignores the restore
    /// SystemCommands.RestoreWindow sends to a disabled window, so nothing came back. And setting WindowState would lose
    /// that it was maximized. Minimized with one of those open, Windows no longer knows it was the last one active, so the
    /// keyboard stays on the window, with what's waiting showing over it for a click.
    /// </summary>
    public static void BringToFront(IntPtr window)
    {
        if (IsIconic(window))
        {
            _ = ShowWindow(window, SwRestore);
        }

        _ = SetForegroundWindow(GetLastActivePopup(window));
    }

    /// <summary>Lets the app go, for whichever copy is next.</summary>
    public void Dispose()
    {
        _listening?.Unregister(null);
        if (_hasApp)
        {
            _hasApp = false;
            _app!.ReleaseMutex();
        }

        _app?.Dispose();
        _comeForward?.Dispose();
        _cameForward?.Dispose();
    }

    /// <summary>What to say about <paramref name="outcome"/>, or null when there's nothing to say.</summary>
    public static string? TextOf(SingleInstanceOutcome outcome) => outcome switch
    {
        SingleInstanceOutcome.NotAnswering => NotAnsweringText,
        SingleInstanceOutcome.OutOfReach => OutOfReachText,
        _ => null,
    };

    /// <summary>What to say when the copy that's open is another build. See <see cref="OpenCopyElsewhere"/>.</summary>
    public static string ElsewhereText(string path) =>
        "TinapayanRMS is already open from another folder:"
        + Environment.NewLine + Environment.NewLine
        + path
        + Environment.NewLine + Environment.NewLine
        + "This copy won't open beside it, because a save in one could overwrite the other's. "
        + "That copy comes to the front next. To use this one instead, close that one first.";

    /// <summary>
    /// Where the copy that's open runs from, when that isn't where this one does: the published app, say, with a new build
    /// being started. Coming to the front without a word would pass the old build off as the new one. Null when it's
    /// from the same place, or when it can't be told: it may just have closed, or be out of reach.
    /// </summary>
    public static string? OpenCopyElsewhere()
    {
        using var current = Process.GetCurrentProcess();
        var copies = Process.GetProcessesByName(current.ProcessName);
        try
        {
            return ElsewhereThan(Environment.ProcessPath, copies.Where(copy => copy.Id != current.Id).Select(PathOf));
        }
        finally
        {
            foreach (var copy in copies)
            {
                copy.Dispose();
            }
        }
    }

    /// <summary>The first of <paramref name="theirs"/> known not to be <paramref name="mine"/>, or null.</summary>
    public static string? ElsewhereThan(string? mine, IEnumerable<string?> theirs) =>
        theirs.FirstOrDefault(path => path is not null && !string.Equals(path, mine, StringComparison.OrdinalIgnoreCase));

    private static string? PathOf(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr GetLastActivePopup(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
