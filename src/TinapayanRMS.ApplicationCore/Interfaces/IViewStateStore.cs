namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// How the app was left and how one person likes it, remembered between runs for the Windows user: which folders of the
/// Products and Raw Materials lists were open, and whether the Vim keys are on. Kept on this computer and never in the
/// database, since it's one person's view of the data rather than the bakeshop's data. Nothing in it is needed: a value
/// never written, or one that can't be read back, reads as null, and the app starts as it would the first time.
/// </summary>
public interface IViewStateStore
{
    /// <summary>The list saved under <paramref name="key"/>, or null if there's none.</summary>
    IReadOnlyList<string>? ReadList(string key);

    /// <summary>Saves <paramref name="values"/> under <paramref name="key"/>, in place of what was there.</summary>
    void WriteList(string key, IReadOnlyList<string> values);
}
