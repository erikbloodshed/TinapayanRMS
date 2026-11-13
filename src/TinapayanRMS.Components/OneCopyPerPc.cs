namespace TinapayanRMS.Components;

/// <summary>
/// One copy of TinapayanRMS on the PC at a time, whoever is signed in. The saves have no concurrency checks, so two copies
/// on the one database would each save over the other's changes without a word: a second Windows user signed in on the
/// shop's PC (fast user switching), or the WPF app it replaced still installed there. A copy is per Windows user (the
/// installer puts it in the user's own folder), so it's found by its running process, not a mutex named after its folder.
/// Within one sign-in, one copy runs per folder (the MAUI app's SingleInstance), so a copy built from source still runs
/// beside the installed one there.
/// </summary>
public static class OneCopyPerPc
{
    /// <summary>The WPF app's process (TinapayanRMS.UI.exe), as Remove-WpfApp.ps1 knows it.</summary>
    public const string WpfProcessName = "TinapayanRMS.UI";

    public const string OtherSignInText =
        "TinapayanRMS is already open in another Windows sign-in on this PC. Two copies would save over each other's "
        + "changes without a word, so only one can be open. Close it in that sign-in, then open it here.";

    public const string WpfAppText =
        "The old TinapayanRMS (the WPF app) is open on this PC. It can't share the database with this one, so close it, "
        + "then open TinapayanRMS again.";

    /// <summary>A process of this app's, or the WPF app's, that's running: its id, its Windows session, and which app it is.</summary>
    public readonly record struct RunningCopy(int Id, int SessionId, bool IsWpfApp);

    /// <summary>
    /// Why this copy (process <paramref name="ownId"/>, in session <paramref name="ownSessionId"/>) mustn't run beside
    /// <paramref name="running"/>, or null when it may: the WPF app open in any session, or this app open in another one.
    /// A copy in this session is the one-copy-per-folder rule's to decide.
    /// </summary>
    public static string? RefusalFor(IEnumerable<RunningCopy> running, int ownId, int ownSessionId)
    {
        string? refusal = null;
        foreach (var copy in running)
        {
            if (copy.Id == ownId)
            {
                continue;
            }

            if (copy.IsWpfApp)
            {
                return WpfAppText;
            }

            if (copy.SessionId != ownSessionId)
            {
                refusal = OtherSignInText;
            }
        }

        return refusal;
    }
}
