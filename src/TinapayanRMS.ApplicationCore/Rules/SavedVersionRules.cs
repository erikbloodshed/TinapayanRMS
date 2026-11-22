using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// Optimistic concurrency for the three records a form edits at length (a product, a raw material, a sub-assembly). A
/// form's Save says when the record it opened had last been saved (its UpdatedAt as the form took it), and the save is
/// refused if it's been saved since: by another PC on the same database, whose save would otherwise be written over unseen.
/// The edits stay on the form, and opening the record again shows what's saved now. Every write takes its turn behind any
/// other (Transactions.BeginOrJoinTransactionAsync), so the time compared is the one the last save committed. A save that
/// says nothing (a new record, or a caller that isn't a form) isn't checked.
/// </summary>
public static class SavedVersionRules
{
    /// <summary>
    /// What a form sends for a saved record it opened with no saved time, one no save has stamped yet: it did open it, so a
    /// save stamped since refuses this one, where null would say nothing was opened. Never stored: a save stamps its own.
    /// </summary>
    public static readonly DateTime NeverSaved = DateTime.MinValue;

    /// <summary>The time a form sends for a saved record it took as saved at <paramref name="savedAt"/>.</summary>
    public static DateTime OpenedAs(DateTime? savedAt) => savedAt ?? NeverSaved;

    /// <summary>
    /// Refuses a save of <paramref name="name"/> when it's been saved since the form opened it: its UpdatedAt stored now,
    /// <paramref name="storedSavedAt"/>, isn't the one the form took, <paramref name="openedSavedAt"/>.
    /// </summary>
    public static void EnsureNotSavedSince(string name, DateTime? storedSavedAt, DateTime? openedSavedAt)
    {
        if (openedSavedAt is not { } opened)
        {
            return;
        }

        DateTime? openedAt = opened == NeverSaved ? null : opened;
        if (storedSavedAt != openedAt)
        {
            throw new SavedSinceException(SavedSinceText(name));
        }
    }

    /// <summary>
    /// A rename or a move from a list, of the record <paramref name="id"/>: refused as the form's Save would be when a form
    /// holds it with unsaved changes (its check among <paramref name="checks"/>, one from each tab holding one of the
    /// records the write takes). A record no form holds with changes isn't checked.
    /// </summary>
    public static void EnsureNotSavedSince(IReadOnlyCollection<SavedVersionCheck> checks, int id, string name, DateTime? storedSavedAt)
    {
        foreach (var check in checks)
        {
            if (check.RecordId == id)
            {
                EnsureNotSavedSince(name, storedSavedAt, check.OpenedSavedAt);
            }
        }
    }

    /// <summary>
    /// Refuses a product's Save that changes the lines of <paramref name="name"/>, a sub-assembly it links, when
    /// <paramref name="storedLines"/> aren't the <paramref name="linesTaken"/> it changed. By the lines rather than the
    /// sub-assembly's UpdatedAt, as they're all such a Save writes there: a rename since, or a save of the same lines, is
    /// nothing to write over. A save that doesn't say what it took isn't checked.
    /// </summary>
    public static void EnsureLinesNotSavedSince(string name, IReadOnlyList<RecipeLine> storedLines, IReadOnlyList<RecipeLine>? linesTaken)
    {
        if (linesTaken is { } taken && !storedLines.SequenceEqual(taken))
        {
            throw new SavedSinceException(LinesSavedSinceText(name));
        }
    }

    /// <summary>What a product's refused Save says of a linked sub-assembly's lines: they can be this window's own save, from another tab.</summary>
    public static string LinesSavedSinceText(string name) => RecipeSavedSinceText($"{name}'s lines were saved");

    /// <summary>
    /// What a product's refused Save says of <paramref name="name"/>, made the product's own since the form took it
    /// (<paramref name="isOwnNow"/>), or no longer its own: its role's Product-owned was changed in Settings.
    /// </summary>
    public static string OwnershipChangedText(string name, bool isOwnNow) =>
        RecipeSavedSinceText(isOwnNow ? $"{name} became this product's own" : $"{name} moved to the Sub-Assemblies screen");

    private static string RecipeSavedSinceText(string happened) =>
        $"{happened} after you opened this product, so your changes weren't saved. They're still on the form: "
        + "note them, press Revert to see what was saved, then make yours again.";

    /// <summary>"Someone else saved Pandesal after you opened it, …": what a refused save says, true of every screen's form.</summary>
    public static string SavedSinceText(string name) =>
        $"Someone else saved {name} after you opened it, so your changes weren't saved. They're still on the form: note them, "
        + $"open {name} again without saving to see what was saved, then make yours again.";
}
