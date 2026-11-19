using TinapayanRMS.Presentation.Printing;

namespace TinapayanRMS.Components.Printing;

/// <summary>
/// What the print preview page (/print/preview) shows: the printouts a screen offered through its ShowPrint (the Price
/// Simulator's choices), or tables (a Reports table as it's shown, a sub-assembly's recipe), and where Back returns to. One
/// per page the window loads; the page is opened by the screen that fills it.
/// </summary>
public sealed class PrintPreviews
{
    public IReadOnlyList<PrintChoice> Choices { get; private set; } = [];

    /// <summary>Tables to show in place of <see cref="Choices"/>; null when it's products.</summary>
    public TablePrintout? Tables { get; private set; }

    /// <summary>The screen that asked, for the preview's Back.</summary>
    public string ReturnTo { get; private set; } = "";

    public void Show(IReadOnlyList<PrintChoice> choices, string returnTo)
    {
        Choices = choices;
        Tables = null;
        ReturnTo = returnTo;
    }

    public void ShowTables(TablePrintout tables, string returnTo)
    {
        Choices = [];
        Tables = tables;
        ReturnTo = returnTo;
    }
}
