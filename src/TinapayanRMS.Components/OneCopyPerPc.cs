namespace TinapayanRMS.Components;

/// <summary>
/// One copy of TinapayanRMS on the PC at a time, whoever is signed in. The saves have no concurrency checks, so two copies
/// on the one database would each save over the other's changes without a word: a second Windows user signed in on the
/// shop's PC (fast user switching), say. A copy is per Windows user (the installer puts it in the user's own folder), so
/// it's found by its running process, not a mutex named after its folder. Within one sign-in, one copy runs per folder
/// (the MAUI app's SingleInstance), so a copy built from source still runs beside the installed one there.
/// </summary>
public static class OneCopyPerPc
{
    public const string OtherSignInText =
        "TinapayanRMS is already open in another Windows sign-in on this PC. Two copies would save over each other's "
        + "changes without a word, so only one can be open. Close it in that sign-in, then open it here.";

    /// <summary>A process of this app's that's running: its id and its Windows session.</summary>
    public readonly record struct RunningCopy(int Id, int SessionId);

    /// <summary>
    /// Why this copy (process <paramref name="ownId"/>, in session <paramref name="ownSessionId"/>) mustn't run beside
    /// <paramref name="running"/>, or null when it may: the app open in another session refuses it. A copy in this
    /// session is the one-copy-per-folder rule's to decide.
    /// </summary>
    public static string? RefusalFor(IEnumerable<RunningCopy> running, int ownId, int ownSessionId) =>
        running.Any(copy => copy.Id != ownId && copy.SessionId != ownSessionId) ? OtherSignInText : null;
}
