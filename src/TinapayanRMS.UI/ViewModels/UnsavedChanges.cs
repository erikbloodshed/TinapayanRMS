namespace TinapayanRMS.UI.ViewModels;

/// <summary>What the user answers when a form with unsaved changes is about to be left (ViewModelBase.ConfirmUnsavedChanges).</summary>
public enum UnsavedChangesAnswer
{
    /// <summary>Save the changes, then go on.</summary>
    Save,

    /// <summary>Throw the changes away, then go on.</summary>
    DontSave,

    /// <summary>Stay on the form, changes and all.</summary>
    Cancel,
}

/// <summary>
/// A screen whose form can hold unsaved changes: Products, Raw Materials and Sub-Assemblies. It's never left with them:
/// the main window asks it before
/// another tab shows or the app closes, and the screen asks itself before its form shows another record.
/// </summary>
public interface IUnsavedChangesScreen
{
    /// <summary>Whether the form holds anything a Save would write.</summary>
    bool HasUnsavedChanges { get; }

    /// <summary>
    /// Asks what to do with the form's unsaved changes, when it has any, and does it: Save saves them, Don't Save throws them
    /// away, and Cancel keeps them. With <paramref name="revert"/>, Don't Save also puts the form back as it was last saved,
    /// for a screen left showing it (another tab picked); without, the form is left for what replaces it (another record,
    /// a new one, the app closing). True when the form may be left: nothing was unsaved, the Save went through, or the user
    /// chose not to save.
    /// </summary>
    Task<bool> ResolveUnsavedChangesAsync(bool revert);
}
