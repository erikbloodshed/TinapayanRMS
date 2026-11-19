using TinapayanRMS.Presentation.Printing;

namespace TinapayanRMS.Components.Printing;

/// <summary>What the Print dialog shows, if it's open.</summary>
public enum PrintPreviewKind
{
    /// <summary>Products' Print: the products ticked (PrintDialogState), or the product Products has open.</summary>
    Products,

    /// <summary>The printouts a screen offered (the Price Simulator's choices).</summary>
    Choices,

    /// <summary>Tables: a Reports table as it's shown, a sub-assembly's recipe.</summary>
    Tables,
}

/// <summary>
/// The Print dialog (PrintDialog, over the window): whether it's open, and on what. Products' Print, its list's Print
/// and the Vim keys' :print open it on the products (<see cref="ShowProducts"/>); the Price Simulator on its printouts
/// (<see cref="Show"/>); Reports and Sub-Assemblies on a table (<see cref="ShowTables"/>). The screen it was opened over
/// stays as it was, unsaved edits and all. One per page the window loads.
/// </summary>
public sealed class PrintPreviews
{
    /// <summary>Raised when the dialog opens, closes or shows something else.</summary>
    public event Action? Changed;

    /// <summary>What the dialog shows; null while it's closed.</summary>
    public PrintPreviewKind? Showing { get; private set; }

    public IReadOnlyList<PrintChoice> Choices { get; private set; } = [];

    /// <summary>The tables the dialog shows, while it shows <see cref="PrintPreviewKind.Tables"/>.</summary>
    public TablePrintout? Tables { get; private set; }

    public void ShowProducts() => Open(PrintPreviewKind.Products, [], null);

    public void Show(IReadOnlyList<PrintChoice> choices) => Open(PrintPreviewKind.Choices, choices, null);

    public void ShowTables(TablePrintout tables) => Open(PrintPreviewKind.Tables, [], tables);

    /// <summary>Closes the dialog: its ✕ or Esc, the window locking, or a move to another address.</summary>
    public void Close()
    {
        if (Showing is null)
        {
            return;
        }

        Showing = null;
        Choices = [];
        Tables = null;
        Changed?.Invoke();
    }

    private void Open(PrintPreviewKind kind, IReadOnlyList<PrintChoice> choices, TablePrintout? tables)
    {
        Showing = kind;
        Choices = choices;
        Tables = tables;
        Changed?.Invoke();
    }
}
