namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// How the screens were left, remembered between runs for the Windows user: which folders of the Products list were
/// open. Kept on this computer and never in the database, since it's one person's view of the data rather than the
/// bakeshop's data. Nothing in it is needed: a value never written, or one that can't be read back, reads as null, and
/// the screen starts as it would the first time.
/// </summary>
public interface IViewStateStore
{
    /// <summary>The list saved under <paramref name="key"/>, or null if there's none.</summary>
    IReadOnlyList<string>? ReadList(string key);

    /// <summary>Saves <paramref name="values"/> under <paramref name="key"/>, in place of what was there.</summary>
    void WriteList(string key, IReadOnlyList<string> values);
}
