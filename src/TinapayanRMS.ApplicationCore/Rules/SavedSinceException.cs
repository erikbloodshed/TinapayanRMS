namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// A form's Save refused because the record has been saved since the form opened it (SavedVersionRules). A refusal like any
/// other, shown as written; a type of its own so the screen can read its lists again, and the record opened next is the one
/// that's saved now (RecordScreenState.RunSaveAsync).
/// </summary>
public sealed class SavedSinceException : BusinessRuleException
{
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
