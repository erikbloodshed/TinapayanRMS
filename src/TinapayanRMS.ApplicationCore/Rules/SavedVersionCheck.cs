namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// The record a form shows with unsaved changes, and when it had last been saved as the form took it
/// (<see cref="SavedVersionRules.OpenedAs"/>): what a rename or a move from the screen's list sends with its write, so that
/// write is refused over a save made since, as the form's own Save would be. Only this record is checked: nothing is typed
/// about the others the write takes.
/// </summary>
public sealed record SavedVersionCheck(int RecordId, DateTime OpenedSavedAt);
