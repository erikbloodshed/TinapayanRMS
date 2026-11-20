namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// A record a form holds with unsaved changes, and when it had last been saved as the form took it
/// (<see cref="SavedVersionRules.OpenedAs"/>): what a rename or a move from a record view's list sends with its write, one
/// from each tab holding one of the records it writes, so that write is refused over a save made since, as the form's own
/// Save would be. Only those records are checked: nothing is typed about the others the write takes.
/// </summary>
public sealed record SavedVersionCheck(int RecordId, DateTime OpenedSavedAt);
