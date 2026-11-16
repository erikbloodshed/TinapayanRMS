using TinapayanRMS.Presentation.Printing;

namespace TinapayanRMS.Components.Printing;

/// <summary>
/// What the print preview page (/print/preview) shows: the printouts a screen offered through its ShowPrint (the Price
/// Simulator's choices), and where Back returns to. One
/// per page the window loads; the page is opened by the screen that fills it.
/// </summary>
public sealed class PrintPreviews
{
    public IReadOnlyList<PrintChoice> Choices { get; private set; } = [];

    /// <summary>The screen that asked, for the preview's Back.</summary>
    public string ReturnTo { get; private set; } = "";

    public void Show(IReadOnlyList<PrintChoice> choices, string returnTo)
    {
        Choices = choices;
        ReturnTo = returnTo;
    }
}
