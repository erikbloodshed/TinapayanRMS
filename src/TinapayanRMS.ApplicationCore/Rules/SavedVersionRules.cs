namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// Optimistic concurrency for the three records a form edits at length (a product, a raw material, a sub-assembly). A
/// form's Save says when the record it opened had last been saved (its UpdatedAt as the form took it), and the save is
/// refused if it's been saved since: by another PC on the same database, whose save would otherwise be written over unseen.
/// The edits stay on the form, and opening the record again shows what's saved now. Every write takes its turn behind any
/// other (Transactions.BeginOrJoinTransactionAsync), so the time compared is the one the last save committed. A save that
/// says nothing (a new record, or a caller that isn't a form) isn't checked, and nor is a record no save has stamped yet.
/// </summary>
public static class SavedVersionRules
{
    /// <summary>
    /// Refuses a save of <paramref name="name"/> when it's been saved since the form opened it: its UpdatedAt stored now,
    /// <paramref name="storedSavedAt"/>, isn't the one the form took, <paramref name="openedSavedAt"/>.
    /// </summary>
    public static void EnsureNotSavedSince(string name, DateTime? storedSavedAt, DateTime? openedSavedAt)
    {
        if (openedSavedAt is { } opened && storedSavedAt != opened)
        {
            throw new SavedSinceException(SavedSinceText(name));
        }
    }

    /// <summary>"Someone else saved Pandesal after you opened it, …": what a refused save says, true of every screen's form.</summary>
    public static string SavedSinceText(string name) =>
        $"Someone else saved {name} after you opened it, so your changes weren't saved. They're still on the form: note them, "
        + $"open {name} again without saving to see what was saved, then make yours again.";
}
