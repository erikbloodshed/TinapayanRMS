namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// The name of a Category picker's "&lt; Add New .. &gt;" row (Raw Materials and Products), which takes in what's typed
/// so it survives the picker's AutoComplete filter, and the reverse: what was typed, from the picker's text.
/// </summary>
public static class AddNewCategoryLabel
{
    private const string Plain = "< Add New .. >";
    private const string Prefix = "< Add New \"";
    private const string Suffix = "\" .. >";

    /// <summary>The row's name for what's typed: "&lt; Add New "X" .. &gt;", or the plain row for nothing typed.</summary>
    public static string For(string typedText)
    {
        var typed = TypedText(typedText);
        return typed.Length == 0 ? Plain : $"{Prefix}{typed}{Suffix}";
    }

    /// <summary>
    /// What was typed, trimmed. Arrowing down the open picker onto the row puts the row's own name in the picker's
    /// text, so that name reads as the text inside it; taking it at face value would wrap the row's name in itself
    /// again at every arrow press, and open the prompt with the whole label in it.
    /// </summary>
    public static string TypedText(string text)
    {
        var typed = text.Trim();

        while (true)
        {
            if (typed == Plain)
            {
                return string.Empty;
            }

            if (typed.Length < Prefix.Length + Suffix.Length
                || !typed.StartsWith(Prefix, StringComparison.Ordinal)
                || !typed.EndsWith(Suffix, StringComparison.Ordinal))
            {
                return typed;
            }

            typed = typed[Prefix.Length..^Suffix.Length].Trim();
        }
    }
}
