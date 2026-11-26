namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// A form's Save refused because the record has been saved since the form opened it (SavedVersionRules). A refusal like any
/// other, shown as written unless the form, having read the record again, puts it otherwise; a type of its own so the screen
/// can read its lists again, and the record opened next is the one that's saved now (RecordScreenState.OnActionFailedAsync).
/// </summary>
public sealed class SavedSinceException : BusinessRuleException
{
    /// <summary>
    /// When a product's Save is refused because a sub-assembly changed owner since (SavedVersionRules.OwnershipChanged): which
    /// one, so the form can say what to do once it's read the product again. Null for any other refusal.
    /// </summary>
    public OwnershipChange? Ownership { get; init; }

    public SavedSinceException()
    {
    }

    public SavedSinceException(string message)
        : base(message)
    {
    }

    public SavedSinceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
